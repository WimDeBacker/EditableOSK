# On-Screen Keyboard

A configurable on-screen keyboard for Windows, driven by XML layout files.

---

## Security — Layout files and the `Send` field

### English

Layout files (`.xml`) are powerful by design. Every key can carry a `Send` value that is passed directly to the Windows `SendKeys` API, which means a crafted layout file can send **arbitrary keystrokes to any application that has focus** — including opening the Run dialog (`Win+R`), typing a command, and pressing Enter.

**Only load layout files from sources you trust.**

There is no built-in sandbox. The application does not filter or restrict what a `Send` value may contain. This is intentional: legitimate layouts need to send modifier combinations, function keys, shortcuts, and special characters, all of which require the full expressive power of `SendKeys`.

If you receive a layout file from an unknown source and want to inspect it safely:
- Open the `.xml` file in a text editor before loading it.
- Review every `Send`, `ShiftSend`, and `AltGrSend` attribute on every `<Key>` element.
- Anything that looks like a shell command, URL, or key sequence you did not expect is a red flag.

---

### Nederlands

Lay-outbestanden (`.xml`) zijn bewust krachtig ontworpen. Elke toets kan een `Send`-waarde bevatten die rechtstreeks aan de Windows `SendKeys`-API wordt doorgegeven. Een kwaadwillig samengesteld lay-outbestand kan daardoor **willekeurige toetsaanslagen sturen naar elke toepassing die de focus heeft** — inclusief het openen van het dialoogvenster Uitvoeren (`Win+R`), het typen van een opdracht en het indrukken van Enter.

**Laad alleen lay-outbestanden van bronnen die u vertrouwt.**

Er is geen ingebouwde beveiliging. De toepassing filtert of beperkt de inhoud van `Send`-waarden niet. Dit is bewust: legitieme lay-outs moeten combinaties van modifiertoetsen, functietoetsen, sneltoetsen en speciale tekens kunnen verzenden, waarvoor de volledige mogelijkheden van `SendKeys` nodig zijn.

Als u een lay-outbestand van een onbekende bron ontvangt en dit veilig wilt inspecteren:
- Open het `.xml`-bestand in een teksteditor voordat u het laadt.
- Controleer elk `Send`-, `ShiftSend`- en `AltGrSend`-attribuut van elk `<Key>`-element.
- Alles wat lijkt op een shellopdracht, URL of toetsreeks die u niet verwacht, is een waarschuwingssignaal.
