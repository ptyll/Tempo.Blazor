# Fáze 6 — kontrakt mobilního režimu

## Stavy
- DataTableMobileLayout: None (výchozí, dnešní markup), Scroll, Cards. Žádné Auto.
- CardMode=true zůstává aliasem pro Cards. CardMode bez MobileLayout nemění dnešní media query.
- TmMultiViewList.Responsive: výchozí false. true přidá tm-mvl-table-scroll.
- CardRole: Field (výchozí), Title/Badge/Media/Hidden opt-in.

## Hranice
- None nesmí změnit existující snapshot tabulky.
- container-type pouze tehdy, když MobileLayout není None.
- App shell, hamburger ani spodní tab bar se neimplementují.

## Testy
TmDataTableMobileLayoutTests a TmMultiViewListResponsiveTests. Výchozí None a Responsive=false musí projít dnešními asercemi.
