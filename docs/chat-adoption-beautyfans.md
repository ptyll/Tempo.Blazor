# BeautyFans adoption notes: migrace Inboxu a fanouškovského chatu na `TmChat`

> **Toto je pouze dokumentace.** Nikdo v tomto kroku nemění kód BeautyFans ani Tempo.Blazor.
> Samotná migrace je samostatný plán v repu BeautyFans — **nezakládejte ho v rámci tohoto
> plánu** (doporučení viz [kapitola 6](#6-doporučení-samostatný-migrační-plán-v-beautyfans)).
>
> Zdroj pravdy o API: [docs/chat-custom-templates.md](chat-custom-templates.md), sekce
> „TmChat“ a „TmChatBubble“ v [COMPONENTS.md](../COMPONENTS.md) a demo `/chat`
> (`src/Tempo.Blazor.Demo.SharedUI/Pages/ChatPage.razor`). Všechny názvy níže jsou ověřené
> proti `src/Tempo.Blazor/Components/Chat/`.

BeautyFans má dva ručně psané chaty:

| Chat | Zdroje | Co si napsala sama |
|------|--------|--------------------|
| Studio Inbox (Owner) | `src/BeautyFans.Web.Client/Components/Pages/Studio/Inbox.razor` (+ `.cs`, `.css`), `src/BeautyFans.Web.Client/Components/Pages/Studio/InboxBubble.razor`, `src/BeautyFans.Web.Client/Components/Pages/Studio/InboxFanPanel.razor` | bubliny AI draftu (Approve/Edit/Discard + badge důvodu), naplánovaná zpráva s odpočtem a Cancel, zahozená zpráva přeškrtnutá s poznámkou, tip pilulka, vlastní composer (owner note + PPV), live region s trailing throttlem (3 s) |
| Fanouškovský chat | `src/BeautyFans.Web.Client/Components/Pages/Fan/Messages.razor` (+ `.cs`), `src/BeautyFans.Web.Client/Components/Shared/Chat/ChatBubble.razor`, `src/BeautyFans.Web.Client/Components/Shared/Chat/TypingIndicator.razor`, `src/BeautyFans.Web.Client/Components/Shared/Chat/ConnectionBar.razor` | bubliny tip/system/locked/mine/theirs + „Přečteno“, subscribe gate, `{name} píše…` s `aria-hidden`, live region s 400 ms throttlem, `bindEnterSubmit` / `focusById` / `fitFanMessages` / `watchViewport` v `src/BeautyFans.Web.Client/wwwroot/js/dom.js` |

`TmChat` 2.10.0 nabízí tohle všechno jako veřejné, testované API (`MessageTemplate`,
`TmChatBubble`, `ComposerTemplate` + `ChatComposerContext`, `BannerContent`, `FillParent`,
`AutoScroll`, live region s `MessageAnnouncement`/`AnnouncementThrottle`/`AnnounceAsync`,
`TypingLabel`/`TypingAriaHidden`/`TypingTimeout`).

**Minimální verze pro migraci: `Tempo.Blazor` 2.10.0** — **zatím NEVYDANÁ**, v době psaní
existuje jen v repu / z lokálního buildu. Šablony, `TmChatBubble`, `FillParent`,
`AutoScroll`, live region i `ChatMessage.Kind/Metadata/Payload` jsou nově 2.10.0; starší verze
nemají žádné `RenderFragment` API.

---

## 1. Studio Inbox — `Pages/Studio/Inbox.razor`

### 1.1 Co se mapuje

Dnešní chat sloupec (`.col-chat`) je `header.ch-head` + `div.chat-scroll` (smyčka `InboxBubble`
+ `TypingIndicator`) + `form.composer`. Migrace:

```razor
@* .col-chat: .ch-head zůstává hostovi (back + persona + fan panel tlačítko),
   chat i composer přecházejí do jedné komponenty *@
<TmChat @ref="chat"
        @key="thread.ConversationId"
        Messages="@_threadMessages"
        MessageTemplate="InboxMessageTemplate"
        FillParent="true"
        ShowAvatars="false"
        EnableReactions="false"
        EnableThreads="false"
        AllowEditDelete="false"
        ShowReadReceipts="false"
        AutoReadReceipts="false"
        CurrentUser="@PersonaUser()"
        TypingUsers="@_typingUsers"
        AutoScroll="ChatAutoScroll.WhenAtBottom"
        AnnouncementThrottle="TimeSpan.FromSeconds(3)"
        MessageAnnouncement="AnnounceInboxMessage"
        TestIdPrefix="inbox"
        OnSend="SendViaChatAsync" />
```

- `@key="thread.ConversationId"` — každá konverzace dostane čerstvou instanci `TmChat`;
  pipeline oznámení, `_seenRootIds` i scroller se nenesou mezi vlákny.
- `CurrentUser="@PersonaUser()"` — owner píše jako persona; rozhoduje o vlastních/příchozích
  bublinách a o filtru `AnnounceOwnMessages`.
- `TypingUsers="@_typingUsers"` — pole držte se STABILNÍ referencí a přiřazujte novou instanci
  JEN na hub typing signál: identická reference s nezměněným obsahem okno `TypingTimeout`
  neprodlužuje, ale nová instance na každém re-renderu roztahuje okno donekonečna.
- `AutoReadReceipts="false"` — reporting přečtení (`OnMessageRead`) zůstává vypnutý spolu se
  `ShowReadReceipts="false"`.
- `FillParent="true"` — `.col-chat` je flex/grid potomek `.inbox`; `.tm-chat--fill` dostane výšku od
  rodiče a vnitřní scroller (`min-height: 0`) pohltí rozdíl. Knob `--tm-chat-min-height` čte
  fill mód s **nulovým** fallbackem — host může výšku vzít úplně. Nahrazuje dnešní
  `.chat-scroll { overflow-y: auto }` uvnitř fixní mřížky.
- `ShowAvatars="false"`, `EnableReactions/EnableThreads/AllowEditDelete/ShowReadReceipts
  ="false"` — inbox nechce žádnou vestavěnou sociální funkci; při `false` nic z dané funkce v
  DOM nezůstává (pinned testy v `tests/Tempo.Blazor.Tests/Components/Chat/`).
- `TestIdPrefix="inbox"` — vestavěné `data-testid` komponenty (`chat-announcements`,
  `chat-load-older`, …) dostanou prefix `inbox-…`, takže E2E selektory zůstanou stabilní a
  jednoznačné (nekolidují s fanouškovským chatem na stejné stránce dashboardu).
- `InboxFanPanel` (`.col-fan` a `TmDrawer`) se **nemigruje** — není součást chatu.
- `.ch-head` může zůstat nad komponentou, nebo jít do slotu `HeaderContent` (pozor: otevřený
  thread panel HeaderContent překrývá — u inboxu jsou vlákna vypnutá, takže je to bezpečné).

### 1.2 `InboxMessageDto` → `ChatMessage`

Kontrakt (zdroj `src/BeautyFans.Shared/Messages/MessageContracts.cs`): `InboxMessageDto(Guid Id,
string Direction, string Body, string Status, DateTime CreatedAt, DateTime? SentAt,
DateTime? SendAfter, bool IsAiGenerated, bool EditedByOwner, string? PolicyReason,
string? SystemKind, Guid? MediaId, decimal? PpvPrice, string? ReviewNote, string Actor,
bool IsParked)`.

`TmChat` zůstává negenerický: host mapuje DTO na `ChatMessage` — `Id`, `Text`, `Author`,
`Type`, `Timestamp` jdou do pozičního konstruktoru, stavová rozšíření do `Kind`/`Metadata`/
`Payload`. `TmChat` nic z toho neinterpretuje; jen zrcadlí `Kind` na wrapperu jako `data-kind`
a `PayloadAs<T>()`/`GetMetadata<T>()` je typovaný přístup šablony.

```csharp
// Inbox.razor.cs — jedno mapování, nic se nemutuje (při změně stavu vzniká NOVÁ instance)
private static ChatMessage ToChatMessage(InboxMessageDto dto) => new(
    dto.Id.ToString("N"),                                            // Id je string
    dto.Body,
    dto.Direction == "FanToCreator" ? FanUser(dto) : PersonaUser(),  // ChatUser (Id, Name)
    dto.Direction == "FanToCreator" ? ChatMessageType.Incoming : ChatMessageType.Outgoing,
    ToTimestamp(dto.SentAt ?? dto.CreatedAt))
{
    Kind = dto.SystemKind == "Tip"
        ? "tip"                                                      // pilulka
        : dto.Status switch
        {
            "PendingReview"       => "ai-draft",                     // → Pending varianta
            _ when dto.IsParked   => "parked",                       // → Queued varianta + err badge
            "Queued"              => "queued",                       // → Queued varianta
            "Discarded"           => "discarded",                    // → Discarded varianta
            _                     => null,                           // null = vestavěná řada
        },
    Metadata = new Dictionary<string, object?>
    {
        ["reason"]    = dto.PolicyReason,   // context.GetMetadata<string>("reason", …)
        ["ppv"]       = dto.PpvPrice,       // context.GetMetadata<decimal?>("ppv")
        ["sendAfter"] = dto.SendAfter,      // odpočet ve šabloně
        ["actor"]     = dto.Actor,          // „aktér · čas“
    },
    Payload = dto,                       // context.PayloadAs<InboxMessageDto>() — [JsonIgnore]
};

private static DateTimeOffset ToTimestamp(DateTime at) =>
    new(at, TimeSpan.Zero);              // API vrací UTC; případně DateTimeKind.SpecifyKind
```

Poznámky:

- `Metadata` je nikdy `null` (default prázdný read-only slovník); hodnoty musí být in-process
  CLR objekty (po host-side JSON round-tripu by byly `JsonElement`).
- `IsParked` nemá vlastní variantu — použijte `Queued` bublinu s červeným badge (stejná sada
  knobů `--tm-chat-bubble-queued-*`), zachová se testid `inbox-parked-{id}`. V mapování musí
  `_ when dto.IsParked` PŘEDCHÁZET `"Queued"` (pořadí: PendingReview, IsParked, Queued,
  Discarded, null) — parked zpráva má status stále `Queued`, takže pozdější větev by ji
  přehlásila na obyčejnou frontu.
- Dnešní `Status = "Sent"` a ostatní běžné zprávy dostanou `Kind = null` — padají na vestavěný
  vzhled přes `UseTemplateFor="m => m.Kind is not null"` (pak se šablona vůbec nevolá), nebo
  je šablona renderuje jako `Incoming`/`Outgoing` bublinu (níže).

### 1.3 `InboxBubble.razor` → `TmChatBubble` varianty

Větve dnešní `InboxBubble.razor` se přehazují na `TmChatBubble` (prezentační komponenta s
vlastním scoped CSS, sloty `Badge`/`Meta`/`Note`/`Actions`, `StatusLabel` pro skryté pojmenování
stavu). **Akce se mění jen v DOM, ne v událostech**: `OnApprove` → `ApproveAsync((Guid Id,
string? Body))`, `OnDiscard` → `AskDiscard(id)` (TmDialog s poznámkou + `ConfirmDiscardAsync`),
`OnCancel` → `CancelAsync(id)` zůstávají přesně jak jsou; šablona jen volá stejné handlery.

| InboxBubble větev | TmChatBubble | Testid |
|-------------------|--------------|--------|
| `SystemKind == "Tip"` → `.msg.system.tip` (StudioBadge gold + `TipText`) | `Pill` | `inbox-tip-{id}` |
| `Status == "PendingReview"` → `.pend` (badge `Reason(PolicyReason)`, edit režim s `TmTextArea`, PPV řádek, Approve/Edit/Discard) | `Pending` | `inbox-pending-{id}` |
| `IsParked` → `.queued.parked` + `inbox-parked-discard` | `Queued` (+ err badge „Parked“) | `inbox-parked-{id}` |
| `Status == "Queued"` → `.queued` (AutoSend badge + `inbox-countdown` + Cancel) | `Queued` | `inbox-queued-{id}` |
| `Status == "Discarded"` → `.msg.discarded` (`.struck` + poznámka) | `Discarded` (tělo je `<del>`, `Note` = ReviewNote) | `inbox-discarded-{id}` |
| jinak `.msg in/out` (+ „aktér · čas“) | `Incoming`/`Outgoing` (nebo `null` Kind → vestavěná řada) | `inbox-msg-{id}` |

Schéma šablony — per-message child komponenta zachová dnešní edit stav i odpočet
(`MessageTemplate` je elementový slot — viz také 1.4 pro atributovou formu):

```razor
@* Inbox.razor — MessageTemplate volá InboxBubble (zůstává per-message komponentou) *@
<TmChat …>
    <MessageTemplate>
        <InboxBubble Message="@(context.PayloadAs<InboxMessageDto>()!)"
                     Now="@now" Busy="@busy"
                     OnApprove="ApproveAsync" OnDiscard="AskDiscard" OnCancel="CancelAsync" />
    </MessageTemplate>
</TmChat>

@* InboxBubble.razor — po migraci; použité pojmenované sloty ⇒ tělo explicitně <ChildContent> *@
<TmChatBubble Variant="@VariantOf(Message)"
              DataTestId="@TestIdOf(Message)"
              ActorLabel="@ActorLabel"
              Timestamp="@ToTimestamp(Message.SentAt ?? Message.CreatedAt)"
              StatusLabel="@StatusLabel"
              Badge="@ReasonBadge"
              Meta="@MetaLine"
              Actions="@Actions">
    <ChildContent>
        @if (Message.Status == "PendingReview" && editing)
        {
            <TmTextArea Id="@EditId" Value="@edited" ValueChanged="EditedChanged" Rows="4"
                        MaxLength="2000" Label="@Loc["Studio.Inbox.Edit"]"
                        AdditionalAttributes="@Attributes("inbox-edit-body")" />
        }
        else
        {
            <p>@Message.Body</p>
        }
    </ChildContent>
</TmChatBubble>
```

**`ChildContent` musí být explicitní, jakmile je kterýkoli slot zapsán jako child element**
(`<Badge>`/`<Meta>`/`<Note>`/`<Actions>`) — pokud žádný slot takto nezapisujete, stačí
`<TmChatBubble …>@tělo</TmChatBubble>`.

Praktická doporučení:

- **Nechte `InboxBubble` jako per-message child komponentu** (vzor výše; srovnej demo
  `ChatDemoQueuedCountdown` v `/chat`): lokální `editing` stav a odpočet potřebují
  per-message state, který by inline Razor lambdou držet nešel.
- Odpočet (`inbox-countdown`, dnes `aria-hidden` + skryté `When`) drží `Now` z page timeru
  (`TickAsync`, 1 s) — `TmChat` žádný odpočet nedělá, čas je hostova doména.
- `TmChatBubble` nese `data-variant="{variant}"` a `data-testid` lze předat parametrem
  `DataTestId` (dědí z `TmComponentBase`) — viz kapitola 4.
- `StatusLabel` je vizuálně skryté pojmenování stavu („AI koncept k revizi“…) a čte se PŘED
  tělem — barva povrchu stav nenazývá.

### 1.4 Composer Inboxu

Dnešní `form.composer`: owner note (`p.owner-note`, testid `inbox-composer-note`) →
`TmTextArea Id="inbox-body"` → PPV pole (`input#inbox-ppv`, jen když
`pageData.PpvMessagesEnabled`) → `TmAlert actionError` → send `inbox-send`. Mapování:

- **Doporučený postup: celý composer do `ComposerTemplate`.** Šablona dostane
  `ChatComposerContext` a zachová dnešní pořadí DOM (note → textarea → PPV → chyba → send).
  `ChatComposerContext` není veřejná vlastnost `TmChat` — kontext zachyťte ve vlastnosti
  `RenderFragment<ChatComposerContext>` (přesně jako demo `/chat`, `CustomComposer`):

```csharp
// Inbox.razor @code
private ChatComposerContext? _composerContext;
private ElementReference _composerBody;
private string? _composerAttachedId;

private RenderFragment<ChatComposerContext> InboxComposerTemplate => composer =>
{
    _composerContext = composer;
    return @<div class="inbox-composer">
        <p class="owner-note xs" data-testid="inbox-composer-note">
            <TmIcon Name="info" Size="IconSize.Sm" />
            <span>@string.Format(Loc["Studio.Inbox.Composer.As"], thread.CreatorDisplayName).
                @Loc["Studio.Inbox.Composer.Note"]</span>
        </p>
        <label for="inbox-body">@Loc["Studio.Inbox.Composer.Body"]</label>
        <textarea id="inbox-body" rows="3" maxlength="2000" data-testid="inbox-body"
                  value="@composer.Text"
                  @oninput="@(e => OnComposerInputAsync(composer, e))"></textarea>
        @if (pageData?.PpvMessagesEnabled == true)
        {
            <label for="inbox-ppv">@Loc["Studio.Inbox.Ppv.Label"]</label>
            <input id="inbox-ppv" type="number" min="3" max="200" step="0.01" value="@ppvPrice"
                   @onchange="PpvChanged" data-testid="inbox-ppv" />
        }
        @if (actionError is { } problem)
        {
            <TmAlert Severity="AlertSeverity.Error" Title="@problem" />
        }
        <TmButton Variant="ButtonVariant.Primary"
                  Disabled="@(composer.Disabled || !composer.CanSend)"
                  OnClick="@(() => SendViaComposerAsync(composer))"
                  AdditionalAttributes="@Attributes("inbox-send")">@Loc["Studio.Inbox.Send"]</TmButton>
    </div>;
};
```

```csharp
// Inbox.razor @code — vstup composeru: SetTextAsync + STÁVAJÍCÍ throttled SignalTypingAsync
// (dnešní OnComposerInput, 4 s guard na lastTypingSignal; composerVersion guard odpadá —
// zdrojem pravdy textu je komponenta a vše běží na jejím dispatcheru)
private async Task OnComposerInputAsync(ChatComposerContext composer, ChangeEventArgs e)
{
    await composer.SetTextAsync(e.Value?.ToString() ?? string.Empty);
    // … dál dnešní throttle: signál jen když uběhly 4 s od posledního (typování je best-effort)
}
```

```razor
<TmChat … ComposerTemplate="InboxComposerTemplate" OnSend="SendViaChatAsync" … />
```

  a v `OnAfterRenderAsync` (a pokaždé, když textarea remountne — viz gate níže):

```csharp
if (_composerContext is not null && !string.IsNullOrEmpty(_composerBody.Id)
    && _composerBody.Id != _composerAttachedId)
{
    _composerAttachedId = _composerBody.Id;
    await _composerContext.AttachAsync(_composerBody); // Enter/Shift+Enter + IME + refocus
}
```

- **Odeslání:** `composer.SendAsync()` je jediná cesta — text ztrimuje, zavolá
  `OnSendMessage`/`OnSend`, po obou handlerech vyčistí composer a vrátí fokus
  (`RefocusAfterSend`, default `true`). Host handler `OnSend="SendViaChatAsync"` čte
  `request.Text` a PPV z hostova stavu (`ParsePrice()`); `body = string.Empty` a
  `composerVersion++`/`composerGeneration++` z dnešního kódu odpadají (čistí komponenta).
- **NEÚSPĚŠNÉ odeslání maže draft.** Composer se vyčistí, jakmile handler VRÁTÍ — a dnešní
  `SendAsync` výjimku spolkne (nastaví `actionError` a vrátí se), takže po migraci by
  neúspěšný send ztratil text. Zvolte jeden ze dvou vzorů:
  - **(a) Handler revehodí, tlačítko chytá.** `SendViaChatAsync` po nastavení `actionError`
    udělá `throw;` — komponenta výjimku vidí a draft NEvyčistí. Kolem
    `composer.SendAsync()` na tlačítku je `catch (ApiException)` (chyba už je zobrazená).
    Na ENTER cestě výjimka projde rendererem (submit z `chat.js` končí
    `DispatchExceptionAsync`) — musí ji spolknout `ErrorBoundary`, takže ho kolem stránky
    počítejte, pokud Enter zůstává zapnutý.
  - **(b) Handler spolkne a host vrátí text.** `SendViaChatAsync` si zapamatuje
    `failedText = request.Text` a v `OnAfterRenderAsync` ho obnoví
    `await _composerContext.SetTextAsync(failedText)` (a vynuluje) — komponenta draft
    mezitím vyčistila, takže uživatel vidí krátce prázdný input a pak vrácený text.

```csharp
// Vzor (a): handler revehodí, tlačítko chytá
private async Task SendViaChatAsync(ChatSendRequest request)
{
    busy = true;
    actionError = null;
    try
    {
        var decision = await InboxApi.SendAsync(thread!.ConversationId,
            new SendOwnerMessageRequest(request.Text, PpvPrice: ParsePrice()), PageLifetimeToken);
        ppvPrice = string.Empty;
        Append(decision.Message);
    }
    catch (ApiException exception)
    {
        actionError = ErrorText(exception);
        throw; // komponenta draft zachová; tlačítko níže chytně, Enter jede rendererem
    }
    finally
    {
        busy = false;
    }
}

private async Task SendViaComposerAsync(ChatComposerContext composer)
{
    try { await composer.SendAsync(); }
    catch (ApiException) { /* actionError už zobrazuje SendViaChatAsync */ }
}
```
- **Remount trik zmizí:** dnešní `@key="composerVersion"` + `composerGeneration` existují kvůli
  `TmTextArea`, který píše value jako child content. S `composer.SetTextAsync("")` (komponenta
  čistí sama) a `AttachAsync` na stejném elementu trik odpadá — element přežije re-render.
- **PAST s `ComposerAddon`:** `ComposerAddon` a `ComposerFooter` se renderují POUZE s vestavěným
  composerem — s nastaveným `ComposerTemplate` se **celé vynechávají** (ověřeno v
  `TmChat.razor`: `stacked = !custom && …`). Tudíž kombinace „owner note do `ComposerAddon`,
  PPV do `ComposerTemplate`“ z původního návrhu fáze NEFUNGUJE; owner note patří DO šablony
  (výše) nebo nad komponentu. Alternativa bez šablony: vestavěný composer + `ComposerAddon`
  (owner note + PPV nad inputem) + `ComposerFooter` (chyba pod inputem) — funguje, ale mění
  pořadí DOM a PPV pole končí nad textarea.
- `ComposerDescribedBy`/hint: vestavěný skrytý hint span (`{inputId}-hint`) je pro built-in
  composer; ve vlastní šabloně si `aria-describedby` řeší host (dnes viditelný `Label` na
  `TmTextArea`, po migraci viditelný `<label for="inbox-body">` — viz výše).

### 1.5 Typing indikátor a ConnectionBar

- **`TypingIndicator` → `TypingLabel` + `TypingAriaHidden="true"` + `TypingTimeout`.** Dnešní
  `TypingIndicator` je `aria-hidden="true"` a píše „{name} píše…“ — `TypingLabel` je
  `Func<IReadOnlyList<ChatUser>, string>` (např. `users => string.Format(Loc["Chat.Typing"],
  users[0].Name)`), `TypingAriaHidden="true"` skryje vestavěný indikátor pro AT a
  `TypingTimeout` ukončí indikátor sám — dnes to řeší `typingUntil` z
  `TypingHubPayload.ExpiresAt` (hub push) + `TickAsync`. Pozor: `TypingTimeout` je FIXNÍ okno
  měřené od poslední změny `TypingUsers` — nastavte ho na TTL, které hub deklaruje v
  `ExpiresAt` (a `TickAsync` pro typing nechte být). Chcete-li zachovat testid
  `inbox-typing` a markup 1:1, použijte `TypingTemplate`: ta si musí `aria-hidden="true"`
  nést SAMA (`TypingAriaHidden` se vztahuje jen na vestavěný indikátor, ne na šablonu) a
  NESMÍ obsahovat live region. Typing se **nikdy neoznamuje**.
- **`ConnectionBar` → `BannerContent` — nebo zůstává na úrovni stránky.** Dnes sedí nad
  sloupci v `Inbox.razor`; buď ho na úrovni stránky PONECHTE (nejmenší změna), nebo ho
  přestaďte do `BannerContent` — jen ne obojí, musí se vyrenderovat PRAVĚ JEDNOU. Slot je
  renderovaný nad seznamem zpráv bez wrapperu:

```razor
<BannerContent>
    <ConnectionBar Offline="offline" Reconnecting="@(Realtime.State == HubConnectionState.Reconnecting)"
                   PollSeconds="PollSeconds" TestId="inbox-offline" />
</BannerContent>
```

  `ConnectionBar` si ponechá `role="status"` — je to **záměrná druhá live region** (hostův
  vlastní element, ne region chatu) a hlásí POUZE stav spojení. Chatův region (viz 1.6)
  hlásí zprávy a akce hosta. Aby nevzniklo duplicitní oznamování: do chatova regionu nikdy
  neposílejte texty o spojení (`AnnounceAsync` jen pro zprávy/akce) a ConnectionBar
  neoznamuje zprávy. Jeden prvek s `role="status"` uvnitř `BannerContent` je v pořádku;
  druhý `role="status"` pro totéž uvnitř šablon ne.

### 1.6 Live region — `announcement` + `announceLifetime` → `MessageAnnouncement` + `AnnouncementThrottle`

Dnes (`Inbox.razor.cs` ~ř. 780–830): `announcement` string + `lastAnnouncement` +
`pendingAnnouncement` + `announceLifetime` CTS = ručně psaný **trailing throttle 3 s**; texty
jsou `Studio.Inbox.Announce.Status` / `Announce.Message` při sendu, decision a realtime
změnách stavu; div `inbox-announcer` je `aria-live="polite"` + `aria-atomic="true"`.

Po migraci:

- **`inbox-announcer` zůstává, jen podmíněně: `@if (thread is null)`.** Dokud není otevřený
  žádný thread (TmChat není namontovaný), oznamuje přes něj pushy do seznamu (`OnConversation`);
  s otevřeným threadem je v DOM JEDEN `aria-live` region — region `TmChat`
  (`data-testid="@TestId("chat-announcements")"` → s `TestIdPrefix="inbox"` →
  `inbox-chat-announcements`). `MessageAnnouncement` pokrývá JEN otevřený thread (zprávy,
  které přistanou do `Messages`); události OSTATNÍCH konverzací (statusy, `OnConversation`)
  routujte přes `await chat.AnnounceAsync(...)`. E2E, které dnes čtou `inbox-announcer`, čtou
  dále jen zprávy z regionu (nebo `role` selektorem). Keyed span uvnitř komponenty zajistí, že
  se stejný text ohlásí znovu BEZ dvoufázového „clear-then-set“ renderu — ten fanouškovský
  kód potřebuje, komponenta ne.
- **`MessageAnnouncement`** = `Func<ChatMessage, string?>` nahradí `Announce(...)` pro PŘÍCHOD
  nových zpráv (realtime push/poll delta). `null` = žádné oznámení. Vlastní (outgoing) zprávy
  se neoznamují (`AnnounceOwnMessages=false` default); inbox je dnes oznamuje
  (`Studio.Inbox.Announce.Message` při vlastním sendu) — buď `AnnounceOwnMessages="true"`,
  nebo po sendu explicitně `await chat.AnnounceAsync(...)`.
- **`AnnouncementThrottle="TimeSpan.FromSeconds(3)"`** replikuje dnešní trailing okno —
  sémantika je shodná (nejnovější text vyhrává, flush na konci okna, volné okno jde hned).
- **Akce a cizí konverzace přes `AnnounceAsync`:** schválení/zahození/cancel/409 konflikt
  jsou hostovy akce — `await chat.AnnounceAsync(Loc["Studio.Inbox.Announce.Status"])` po
  dokončení handleru (`ApproveAsync`, `ConfirmDiscardAsync`, `CancelAsync`,
  `SendViaChatAsync`); realtime pushy, které se netýkají otevřeného threadu (statusy,
  `OnConversation` pro seznam), jdou stejnou cestou. `AnnounceAsync`
  ve volném okně oznámí hned a zahodí pending zprávy z právě utraceného okna (akce je novinka).
- Statusy realtime událostí (Queued→Sent, Parked, Discarded) jsou „změna existující zprávy“ —
  TmChat klasifikuje změny podle first/last root Id, takže nahrazení zprávy stejným Id není
  „nová zpráva“ a neoznamuje se: opět `AnnounceAsync`.

---

## 2. Fanouškovský chat — `Pages/Fan/Messages.razor`

### 2.1 `.fan-messages__scroll` + `ChatBubble.razor` → `MessageTemplate`

```razor
<TmChat @ref="chat"
        @key="thread.ConversationId"
        Messages="@_threadMessages"
        MessageTemplate="FanMessageTemplate"
        FillParent="true"
        ShowAvatars="false"
        EnableReactions="false"
        EnableThreads="false"
        AllowEditDelete="false"
        ShowReadReceipts="false"
        AutoReadReceipts="false"
        CurrentUser="@FanSelf"
        AutoScroll="ChatAutoScroll.WhenAtBottom"
        TypingAriaHidden="true"
        MessageAnnouncement="AnnounceFanMessage"
        AnnouncementThrottle="TimeSpan.FromMilliseconds(400)"
        TestIdPrefix="fan-messages" />
```

- `CurrentUser="@FanSelf"` — fanoušek jako aktuální uživatel: klasifikace vlastních/příchozích
  bublin a filtr `AnnounceOwnMessages`.
- `@key="thread.ConversationId"` — fanoušek přepíná konverzace přes `/messages/{Handle}`;
  čerstvá instance nuluje pipeline oznámení a `_seenRootIds`.
- `AutoReadReceipts="false"` — reporting přečtení (`OnMessageRead`) zůstává vypnutý spolu se
  `ShowReadReceipts="false"`; „Přečteno“ nese hostův `Meta` slot (níže).

Větve `ChatBubble.razor` → `TmChatBubble`:

| ChatBubble větev | TmChatBubble | Testid |
|------------------|--------------|--------|
| `SystemKind == "Tip"` → `bubble--system` (`TipText`, `TipAmount`/`TipCurrency`) | `Pill` | `chat-tip-{id}` |
| jiný `SystemKind` (Welcome text z `PersonaName`) → `bubble--system` | `System` | `chat-system-{id}` |
| `Locked` → `bubble--locked` (jen cena, žádná akce dnes neexistuje, tělo se nikdy nevyrenderuje) | `Locked` (cena do `Badge`, `StatusLabel` pro skryté pojmenování stavu — pro `Locked` TmChatBubble vyžaduje viditelný `Badge` NEBO `StatusLabel`) | `chat-locked-{id}` |
| `Mine` → `bubble--mine` | `Outgoing` | `chat-msg-{id}` |
| `!Mine` → `bubble--theirs` | `Incoming` | `chat-msg-{id}` |

- **„Přečteno“ („Chat.Read“, dnes `bubble__read` když `Mine && ReadAt is not null`) jako
  `Meta` slot** — `Timestamp="@ToTimestamp(message.CreatedAt)"` vyrenderuje `<time>` a `Meta`
  je další span téže meta řady. Vestavěné receipts jsou vypnuté (`ShowReadReceipts="false"`,
  plus žádné `ReadBy` v mapování), takže žádný duplicitní „přečteno“ stroj.
- Běžné zprávy lze nechat na vestavěném vzhledu (`UseTemplateFor="m => m.Kind is not null"`
  + `Kind` jen pro tip/system/locked) — ale dnešní vzhled (`bubble--mine/theirs`, 70 % max
  šířka) je stejně vlastní, takže věrná migrace šablonuje VŠECHNO.
- Wrapper řady (vestavěný, mimo šablonu) zůstává: `data-message-id="{Id}"` + `data-kind`
  — scroll kotva, `FocusMessageAsync(id)` a E2E selektory `[data-message-id="…"]` fungují dál.
- Prázdný thread → `EmptyContent` s DNEŠNÍM `TmEmptyState` (`Fan.Messages.Empty.Thread` +
  `Fan.Messages.Empty.Thread.Text`). TmChat renderuje slot jen při prázdných `Messages`;
  dnešní podmínku `thread.CanWrite && thread.CreatorIsActive` udržte uvnitř slotu (gate a
  inactive stav řeší `ComposerTemplate`).

### 2.2 Subscribe gate a neaktivní vztah → `ComposerTemplate` / `ShowComposer="false"`

Dnes: `!thread.CreatorIsActive` → `TmAlert Info` pod scrollerem; `!thread.CanWrite` →
`div.fan-messages__gate` (testid `fan-messages-gate`) s `SubscribeLabel`/`GateText` a tlačítkem
`fan-messages-subscribe`; composer form jen pro zápis-capable fanoušky.

- **Gate i alert jde do `ComposerTemplate`** — šablona se renderuje uvnitř composer oblasti
  i bez vstupu; větví se podle stavu threadu a NIC neattachuje (žádný `AttachAsync` = žádný
  Enter/IME wiring, což je přesně chování pro non-input obsah):

```razor
<ComposerTemplate>
    @if (thread is null || !thread.CreatorIsActive)
    {
        <TmAlert Severity="AlertSeverity.Info" Title="@Loc["Fan.Messages.Inactive"]" />
    }
    else if (!thread.CanWrite)
    {
        <div class="fan-messages__gate" data-testid="fan-messages-gate">
            <div>
                <h3>@Loc["Fan.Messages.Subscribe.Title"]</h3>
                <p>@GateText</p>
            </div>
            <TmButton Variant="ButtonVariant.Primary" OnClick="Subscribe"
                      AdditionalAttributes="@Attributes("fan-messages-subscribe")">
                @SubscribeLabel
            </TmButton>
        </div>
    }
    else
    {
        @* vlastní composer jako v kapitole 1.4 (fan-messages-body / fan-messages-send) *@
    }
</ComposerTemplate>
```

- **PAST:** `ComposerFooter` se renderuje jen s vestavěným composerem (`stacked` větev) a
  `ShowComposer="false"` skryje celou oblast včetně addonů — kombinace „`ShowComposer=false`
  + `ComposerFooter`“ nedává smysl. Chcete-li gate ÚPLNĚ mimo komponentu, dejte `ShowComposer
  ="false"` a renderujte gate pod `<TmChat>` hostem (DOM pořadí se zachová); `ComposerFooter`
  má smysl jen pro chybu pod vestavěným inputem.
- **Chyba odeslání (`fan-messages-error`, dnes `role="alert"`):** ve vlastní šabloně ji
  renderujte vizuálně (`TmAlert` nese `role="alert"` — je to hostův VLASTNÍ region) a pak
  NEoznamujte totéž přes `AnnounceAsync` — jedna věc = jedna region. Buď vizuální alert
  (ohlásí se sám), nebo `await chat.AnnounceAsync(sendError)` bez `role="alert"`.

### 2.3 JS z `src/BeautyFans.Web.Client/wwwroot/js/dom.js` → vestavěné chování

| Dnes (`dom.js`) | Po migraci |
|-----------------|------------|
| `bindEnterSubmit("fan-messages-body")` — Enter submit, Shift+Enter nový řádek, `event.isComposing` guard | Parametr `EnterToSend` (default `true`) + IME guard v `chat.js` (net8 `KeyboardEventArgs` nemá `IsComposing` — proto JS; C# fallback je za `#if NET9_0_OR_GREATER`). Pro vlastní composer to dělá `composer.AttachAsync(element)`. |
| `focusById(id)` po akcích | `FocusComposerAsync()` (fokus na input) a `FocusMessageAsync(id)` (fokus na řadu zprávy, `block:'nearest'` + reduced-motion); v šabloně composeru `composer.FocusAsync()`. |
| `fitFanMessages(root)` — měří `innerHeight - top - 16` a pinuje `height`/`maxHeight` na desktopu | **`FillParent`** — komponenta je flex child (`min-height: 0`), rodič vlastní výšku; měření a `resize`/`ResizeObserver` listenery mizí. |
| `watchViewport("(max-width: 47.5rem)", …)` + `SetNarrow` pro `composerRows` | Vestavěná responsivita: `.tm-chat` je CSS size container. Cíle 44 px přijdou z `(hover: none), (pointer: coarse)` — NE z breakpointu; na 640px kontejnerovém breakpointu závisí jen sticky composer a celoplošný thread overlay. `composerRows` nahrazuje auto-resize composeru (do kapu 5 řádků přes `chat.js`). Media query nad viewportem už host nepotřebuje. |

Co z `fitFanMessages` **zůstává hostovi** (carry-forward):

- **iOS vizuální klávesnice:** `position: sticky; bottom: var(--tm-chat-composer-offset)`
  sám nesleduje `visualViewport` — malý JS listener na `visualViewport.resize/scroll`,
  který během otevřené klávesnice upraví `--tm-chat-composer-offset`, je stále hostova
  záležitost (knihovna to nedělá, viz Layout notes a CHANGELOG 2.10.0).
- **Bottom navigation:** host nastaví `--tm-chat-composer-offset` na výšku spodní navigace
  fanouška (sticky composer i thread řada sedí nad ní) a **`--tm-chat-safe-area-bottom: 0`**,
  pokud fanouškovská navigace safe area (`env(safe-area-inset-bottom)`) už odsadí sama —
  jinak by se padding pod composerem zdvojnásobil. Oba knoby deklarujte na HOSTOVÁM wrapperu
  (`:root`, layout wrapper kolem chatu) — nikdy na `.tm-chat` v knihovně (knihovna žádné
  defaulty nedeklaruje, fallbacky jsou sémantické tokeny).
- Shrnutí: `FillParent` + `--tm-chat-composer-offset` (+ volitelně `--tm-chat-safe-area-bottom: 0`)
  nahrazují `fitFanMessages` JEN tehdy, když ke chatu vede definitivní výšková flex/grid řada
  (`min-height: 0` v každém patře `.fan-messages__split` → `.fan-messages__chat`); jinak
  nechte `fitFanMessages` na rootu sekce. Jediné, co po hostovi zůstává VŽDY, je iOS
  visualViewport listener.

### 2.4 Typing

`TypingIndicator` (testid `fan-messages-typing`, `aria-hidden="true"`, „{name} píše…“) →
stejně jako u inboxu: `TypingLabel` + `TypingAriaHidden="true"` + `TypingTimeout`. Pro
zachování přesného testidu a markupu použijte `TypingTemplate` (dostává
`IReadOnlyList<ChatUser>`): šablona si musí `aria-hidden="true"` nést SAMA
(`TypingAriaHidden` se vztahuje jen na vestavěný indikátor, ne na šablonu) a NESMÍ obsahovat
live region. Dnešní `typingUntil` expiraci řeší `TypingTimeout` (interní `TimeProvider` —
testy přes `FakeTimeProvider`); nastavte ho na TTL, které hub deklaruje v
`TypingHubPayload.ExpiresAt`.

### 2.5 Live region — `announcement` + `announceDelay` → `MessageAnnouncement` + `AnnouncementThrottle`

Dnes (`Messages.razor.cs` ~ř. 388–415): div `fan-messages-announcer` (`aria-live="polite"`) +
trailing throttle 400 ms s dvoufázovým clear-then-set (identický text se musí oznámit znovu).
Po migraci: `fan-messages-announcer` ZŮSTÁVÁ, jen podmíněně — `@if (thread is null)` (bez
namontovaného chatu); s otevřeným threadem je v DOM jeden region — region chatu. Nastavte
`AnnouncementThrottle="TimeSpan.FromMilliseconds(400)"`, `MessageAnnouncement` pro nové
zprávy OTEVŘENÉHO threadu (realtime push „{SenderName}“ dnes) a `AnnounceAsync` pro akce
(`Fan.Messages.Sent`, chyba odeslání) i pro pushy OSTATNÍCH konverzací — explicitní
oznámení otevřeného threadu z `OnMessage` pusťte, `MessageAnnouncement` by ho zdvojil.
Keyed span regionu dělá clear-then-set za vás.

---

## 3. Scroll a stránkování

### 3.1 Stav dnes

Oba chaty dnes **načítají celé vlákno** (`ThreadAsync(handle/conversationId, after: null)`) a
`afterCursor` (Id poslední zprávy) slouží VÝHRADNĚ pro realtime deltu: `RefreshThreadAsync()`
si vyžádá `ThreadAsync(…, afterCursor)` a zprávy **připojí na konec** (`Append`, dedupe per
Id). Žádné stránkování starších zpráv („načíst starší“) dnešní kód nemá.

### 3.2 `OnLoadOlder` / `HasMoreBefore` na `before` kurzor

`TmChat` umí: `OnLoadOlder` (`EventCallback` — volejte API), `HasMoreBefore` (existuje starší
stránka?), `IsLoadingOlder` (in-flight mirror; komponenta deaktivuje trigger). Postup:

1. Rozšiřte API o stránku po kurzoru **zpětně** — např. `ThreadAsync(…, Guid? before,
   int take)`; dnes existuje jen `after` (delta). První load zůstává celý (nebo první
   „stránka“ + `HasMoreBefore`).
2. `OnLoadOlder` handler: zavolejte API s `before` = dnešní first Id — pozor, `Messages.First().Id`
   je string v „N“ formátu, API čeká `Guid?`: použijte `Guid.ParseExact(…, "N")`, nebo čtěte
   Id z `Payload` (DTO). Dostanete starší stránku a **předřaďte ji** — nový seznam
   `starší + stávající`, poslední Id zůstává. „Reset“ (přistání na konci, ztráta pozice
   čtení i počtu nenačtených) nastane JEN tehdy, když nepřežije ani jeden z okrajových Id —
   výměna seznamu, po které poslední Id přežije, se klasifikuje jako append (sliding window,
   viz 3.3).
3. `HasMoreBefore` = API říká, že existuje další starší stránka; po vyčerpání `false` (trigger
   zmizí). `IsLoadingOlder` držte `true` po dobu requestu.
4. Komponenta detekuje **prepend** (první Id se změnilo, poslední zůstal) a NESCROLLUJE —
   vizuální pozice se obnoví na první viditelnou řadu (kotva prvního řádku, fallback
   delta-výšky).

### 3.3 Realtime delta = append na konec

- Delta push (SignalR, dnes `Append` s dedupe) zůstává: **přidejte zprávu na konec** — nový
  seznam `[.. Messages, msg]`. S `AutoScroll="WhenAtBottom"` (default 2.10.0): kdo čte na
  konci, jede s ním; kdo čte výš, dostane zprávy za tlačítko „nové zprávy“
  (`ShowJumpToNewButton`, počet přes `Loc.Plural("TmChat_NewMessages", …)`, custom obsah
  přes `JumpToNewTemplate`). Vlastní odeslané zprávy scrollují vždy.
- **Prerender nesahá:** před interaktivitou JS scroller není attachnutý a každý automatický
  scroll je no-op; první interaktivní render dojede na konec. (Dnešní `ScrollToBottomAsync` byl
  no-op; v 2.10.0 je funkční a explicitní volání fungují v každém módu.)
- **Sliding window = Append:** když host kapuje délku seznamu (přidá na konec A zahodí
  nejstarší hlavu), klasifikace podle boundary Id — starý poslední Id je UVNITŘ seznamu — to
  se počítá jako **append** (počet nových = řady za starým posledním Id, NE rozdíl počtů).
  Truncování hlavy je tedy bezpečné: nerozhodí „reset“ a nesežere reading position.
- Poll fallback (`PollAsync`, `PollSeconds`) nechte; dedupe per Id (dnešní `Append` guard)
  zůstává hostovu API vrstvě.

---

## 4. data-testid — co si migrace musí zachovat

E2E BeautyFans selektují testidy. Zachovejte je **v šablonách** přes `DataTestId` na
`TmChatBubble` (dědí z `TmComponentBase`, kde `DataTestId` přepisuje default `chat-bubble`) a
`@attributes`/`AdditionalAttributes` na hostovych tlačítkách. Vestavěné prvky komponenty se
namespacují přes **`TestIdPrefix`** (`{prefix}-{name}`) — a účelem jsou STABILNÍ A
JEDNOZNAČNÉ E2E selektory: deterministický prefix, žádná kolize mezi oběma chaty na jedné
stránce ani s jinými komponentami (`chat-announcements`, `chat-load-older`,
`chat-jump-to-new`, …).

### Studio Inbox

| Testid | Kde bude po migraci | Jak zachovat |
|--------|---------------------|--------------|
| `inbox-pending-{id}` / `inbox-queued-{id}` / `inbox-discarded-{id}` / `inbox-tip-{id}` / `inbox-parked-{id}` / `inbox-msg-{id}` | kořen `TmChatBubble` | `DataTestId="@($"inbox-…-{dto.Id}")"` |
| `inbox-countdown` | odpočet ve šabloně | `data-testid` na spanu ve šabloně (nechte `aria-hidden`, skryté `When` zůstává) |
| `inbox-approve` (+ `id="inbox-approve-{id-N}"`), `inbox-edit`, `inbox-edit-cancel`, `inbox-edit-body`, `inbox-discard`, `inbox-parked-discard`, `inbox-cancel` | tlačítka v `Actions` slotu / edit režimu | `AdditionalAttributes="@Attributes(…)"` — události beze změn |
| `inbox-composer-note`, `inbox-body`, `inbox-ppv`, `inbox-send` | `ComposerTemplate` | nechat Id/testidy na `<textarea id="inbox-body">`/inputu/tlačítku (viz 1.4) |
| `inbox-typing` | `TypingTemplate` | vlastní markup s testidem (vestavěný indikátor žádný testid nemá) |
| `inbox-offline` | `BannerContent` | `ConnectionBar TestId="inbox-offline"` zůstává |
| `inbox-announcer` | ZŮSTÁVÁ, jen bez otevřeného threadu (`@if (thread is null)`, viz 1.6); s threadem jedna live region = chatova | E2E s otevřeným threadem → region chatu (`inbox-chat-announcements` s `TestIdPrefix="inbox"`) |
| `inbox-page`, `inbox-live`, `inbox-search`, `inbox-creator`, `inbox-filter-*`, `inbox-row-*`, `inbox-unread-count`, `inbox-badge-parked`, `inbox-empty`, `inbox-thread`, `inbox-thread-empty`, `inbox-loading`, `inbox-error`, `inbox-back`, `inbox-fan-open`, `inbox-discard-note`, `inbox-discard-confirm` | zůstávají hostovi (list, dialogs, fan panel) | beze změn |

### Fan chat

| Testid | Kde bude po migraci | Jak zachovat |
|--------|---------------------|--------------|
| `chat-msg-{id}` / `chat-locked-{id}` / `chat-tip-{id}` / `chat-system-{id}` | kořen `TmChatBubble` | `DataTestId` (dnešní prefix `chat-` se rovná defaultu — stačí `DataTestId="…"` bez změny selektoru) |
| `fan-messages-gate`, `fan-messages-subscribe`, `fan-messages-body`, `fan-messages-send`, `fan-messages-error`, `fan-messages-typing` | `ComposerTemplate` / `TypingTemplate` | testidy přímo v šabloně |
| `fan-messages-offline` | `BannerContent` | `ConnectionBar TestId` zůstává |
| `fan-messages-announcer` | ZŮSTÁVÁ, jen bez otevřeného chatu (`@if (thread is null)`, viz 2.5) | E2E s otevřeným chatem → region chatu (`fan-messages-chat-announcements` s `TestIdPrefix="fan-messages"`) |
| `fan-messages` (root sekce), `fan-messages-live`, `fan-messages-back`, `fan-messages-profile`, `fan-messages-row-*`, `fan-messages-retry`, `fan-messages-discover`, `fan-messages-thread`, `fan-messages-empty`… | zůstávají hostovi | beze změn |

Kotva zprávy pro E2E je **`data-message-id="{Id}"`** (a `data-kind`) — to je vestavěný wrapper
řady, který šablona nemůže odstranit (scroll kotvy, `FocusMessageAsync`, receipts).

---

## 5. Rizika a pořadí migrace

**Pořadí: (1) fanouškovský chat, (2) Studio Inbox.** Fan chat je jednodušší: jeden
`MessageTemplate` (bez decision akcí a 409 konfliktů), jeden composer, gate jako šablona,
žádný per-message edit stav. Inbox přidává per-message edit/odpočet, dialog s poznámkou,
`NextFocus` fokus management (fokus zůstává na `inbox-approve-{id}`; jen větev `inbox-body`
přechází na composer — viz 5.2) a vlastní akce.

### 5.1 Scoped CSS → `--tm-chat-bubble-*` knoby

Dnešní styly (`src/BeautyFans.Web.Client/Components/Pages/Studio/InboxBubble.razor.css`,
`src/BeautyFans.Web.Client/Components/Shared/Chat/ChatBubble.razor.css`,
`src/BeautyFans.Web.Client/Components/Shared/Chat/TypingIndicator.razor.css`)
jsou scoped na hostovů markup. Po migraci je markup v šablonách — a šablona nese **CSS scope
hosta**, ne `TmChat.razor.css` (to je ta známá past). Scoped CSS `TmChatBubble` přitom platí
na prvky, které bublina SAMA renderuje (kořen, meta řada, `<del>` tělo, note/actions row);
obsah slotů (`Badge`/`Meta`/`Note`/`Actions`, `ChildContent`) nese scope hosta. Bubliny tedy
stavte z `TmChatBubble` a BeautyFans vzhled přehoďte na knoby **deklarované na hostovém
wrapperu** (layout wrapper kolem `<TmChat>`), nikdy na `.tm-chat` v knihovně. A pozor:
kořenový rámec `.tm-chat` (background, border, radius, `min-height` floor) se VNOŘUJE do
`.col-chat` — neutralizujte ho přes `::deep .tm-chat { … }` v hostově scoped CSS, nebo
přes parametr `Class` (třída na kořeni, např. i `tm-chat--no-container`):

```css
/* src/BeautyFans.Web.Client/wwwroot — wrapper kolem <TmChat> */
.fan-chat-host {
    --tm-chat-bubble-max-width: 70%;             /* dnešní .bubble max-width */
    --tm-chat-bubble-radius: var(--tm-radius-lg);
    --tm-chat-bubble-padding: var(--tm-space-2) var(--tm-space-3);
    --tm-chat-composer-offset: 4.5rem;           /* výška spodní navigace fanouška */
    --tm-chat-safe-area-bottom: 0;               /* navigace safe area odsadí sama */
}
```

- Dark mode zdarma: defaulty knobů jsou sémantické tokeny, flipují s `tokens-dark.css`.
  Dnešní `[data-theme="dark"] .msg.discarded { outline: dashed … }` odpadá — `Discarded`
  varianta má vlastní sadu knobů (`--tm-chat-bubble-discarded-*`) s borderem.
- `Pill` je shrink-to-fit `inline-flex` (sahá jen na svůj obsah) s `max-width: 100 %` a plným
  radius (`--tm-radius-full`) — dnešní `.msg.system` (gold badge + text) se tedy nemusí ničeho bát.
- Typing styl (`TypingIndicator.razor.css`) → `TypingTemplate` si nese hostovy třídy
  (scoped platí — šablona je hostí), nebo vestavěné tečky (`tm-chat__typing-*`).
- `.chat-scroll` overflow zmizí — vnitřní scroller `.tm-chat__messages` řeší `min-height: 0`
  sám; podmínkou je, že `.col-chat`/layout dává chatu **definitivní výšku** (grid/flex s
  `min-height: 0`), což `FillParent` očekává.

### 5.2 Ostatní rizika

| Riziko | Mítigace |
|--------|----------|
| Live region se ztratí nebo zdvojí | `inbox-announcer`/`fan-messages-announcer` renderujte jen bez otevřeného chatu (`@if (thread is null)`); `MessageAnnouncement` pokrývá JEN otevřený thread, události ostatních konverzací a akce jdou přes `chat.AnnounceAsync(...)` (viz 1.6, 2.5) |
| Neúspěšný send smaže draft | handler dnes výjimku spolkne, takže `composer.SendAsync()` čistí i při chybě: (a) handler revehodí a `catch` sedí kolem `SendAsync()` na tlačítku (Enter cesta projde rendererem → `ErrorBoundary`), nebo (b) handler spolkne a host obnoví `failedText` přes `SetTextAsync` v `OnAfterRenderAsync` (viz 1.4) |
| `FillParent` bez definitivní výšky | funguje jen s flex/grid řadou s `min-height: 0` až ke chatu; jinak zůstává `fitFanMessages` na rootu sekce (viz 2.3) |
| `TmTextArea` remount (`composerVersion`) | zmizí — `composer.SetTextAsync` + `AttachAsync` na jednom elementu |
| `busy` flag | `Disabled` na `<TmChat>` (i `composer.Disabled` pro send button) |
| 409 konflikt (`actionError`) | `TmAlert` v šabloně + `AnnounceAsync` (jedna z nich, ne obě) |
| `NextFocus()` po rozhodnutí | fokus zůstává na `inbox-approve-{id}` (tlačítko přežije v šabloně — dnešní `focusById`); jen větev `inbox-body` jde přes `await chat.FocusComposerAsync()` (`FocusMessageAsync` cílí na řadu zprávy, ne na tlačítko) |
| Změna stavu = nová instance | mapování nikdy nemutuje; `Replace(decision.Message)` → nový `ChatMessage` se stejným Id |
| Generika | `TmChat` zůstává negenerický; DTO putuje přes `Payload`/`PayloadAs<T>()` |
| Auto-attach inputu | dnešní cesta je host volá `AttachAsync` v `OnAfterRenderAsync`; future work je `id="@composer.InputId"` auto-attach (viz docs/chat-custom-templates.md) |
| Verze | **Tempo.Blazor ≥ 2.10.0** (MessageTemplate, TmChatBubble, FillParent, AutoScroll, live region API, Kind/Metadata/Payload) — 2.10.0 zatím NEVYŠLA |

---

## 6. Doporučení: samostatný migrační plán v BeautyFans

Tento dokument je jen mapa. Samotnou migraci (kód + E2E v BeautyFans) založte jako
**samostatný plán v repu BeautyFans** — v rámci tohoto plánu NEZAKLÁDAT. Navrhovaný
rozsah: fáze „fan chat“ (šablona + composer/gate + JS mazání + E2E), fáze „Studio Inbox“
(šablona + decision akce + composer + E2E), fáze „úklid“ (mazání `dom.js` helperů,
starých CSS souborů a duplicitních live regionů). Výchozí stav je popsán v kapitolách 1–5.

## 7. DoD

- [x] Dokument `docs/chat-adoption-beautyfans.md` existuje a pokrývá oba chaty.
- [x] Studio Inbox: smyčka `InboxBubble` → `TmChat` + `MessageTemplate` + `FillParent` +
      vypnuté vestavěné funkce; mapování `InboxMessageDto` → `ChatMessage` (Id „N“, Kind ze
      Status/SystemKind, Metadata PolicyReason/PpvPrice/plánovaný čas, Payload = DTO);
      větve → `TmChatBubble` Pending/Queued/Discarded/Pill + akce beze změny eventů.
- [x] Composer Inboxu: owner note + PPV + chyba → `ComposerTemplate` (a dokumentovaná past
      s `ComposerAddon`), `ChatComposerContext.SendAsync`; `TypingIndicator` → `TypingLabel`
      + `TypingAriaHidden` + `TypingTimeout`; `ConnectionBar` → `BannerContent` s
      `role="status"` a bez duplicitního oznamování.
- [x] Live region: `announceLifetime`/`announceDelay` → `MessageAnnouncement` +
      `AnnouncementThrottle`, akce i události ostatních konverzací přes `AnnounceAsync`,
      `inbox-announcer` / `fan-messages-announcer` jen bez otevřeného chatu
      (`@if (thread is null)` — s chatem jedna region).
- [x] Fan chat: `ChatBubble` → `TmChatBubble` Locked/Pill/Incoming/Outgoing + „Přečteno“ jako
      `Meta`; gate/inactive → `ComposerTemplate`/`ShowComposer="false"`; JS `bindEnterSubmit`,
      `focusById`, `fitFanMessages`/`watchViewport` → `EnterToSend`, `FocusComposerAsync`,
      `FillParent` + `--tm-chat-composer-offset`; host-side zbytky popsány (iOS
      visualViewport, safe area 0).
- [x] Scroll a stránkování: `OnLoadOlder`/`HasMoreBefore` na `before` kurzor, delta append,
      `AutoScroll WhenAtBottom` + tlačítko nových zpráv, sliding window = Append.
- [x] data-testid: kompletní seznam + `TestIdPrefix`.
- [x] Rizika, pořadí (fan chat → inbox), CSS → knoby, minimální verze 2.10.0.
- [x] **Žádná změna kódu v tomto kroku** — jen dokument.
