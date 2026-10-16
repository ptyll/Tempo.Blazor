# Fáze 7 — kontrakt mobilních overlayů

## Stavy
- MobilePresentation=Auto jen když je tabulka v MobileLayout jiném než None, nebo MVL Responsive=true.
- Mimo responsive režim se prezentace nemění.
- TmFilterBuilder.Presentation default zůstává Inline. Auto je opt-in. V dialogu pohledu je filtr vždy Inline.
- ShowFilterSummary default false.

## Hranice
- Nepřepisuj existující sheet primitiv. Použij MobilePresentation, které už komponenty mají.
- Žádný sheet nad sheetem.
- App shell, hamburger a spodní tab bar se neimplementují.

## Testy
TmDataTableMobileOverlayTests, TmDataTableMobileFiltersTests, TmViewManagerSheetTests. Výchozí režim musí projít dnešní aserce.
