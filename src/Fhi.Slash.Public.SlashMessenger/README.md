<!--
  To generate a PDF from this markdown file, use the extension "Markdown PDF" in Visual Studio Code.
  You may change the header and footer template in the settings of the extension.

  Current settings (Add these to settings.json directly):
  "markdown-pdf.headerTemplate": "<div></div>",
  "markdown-pdf.footerTemplate": "<div style=\"font-size: 9px; margin-left: auto; margin-right: 1cm; \"><span class='pageNumber'></span> / <span class='totalPages'></span></div>",
-->

<p align="right">
    <img src="../../docs/images/fhi-logo.svg" alt="FHI Logo" width="100" style=""/>
</p>

# SLASH Messenger (NuGet-pakke)

Dette dokumentet beskriver strukturen og oppbygningen av prosjektet `Fhi.Slash.Public.SlashMessenger`.
Koden er skrevet i .NET 8 (C#) og er tilgjengelig som en NuGet-pakke (`Fhi.Slash.Public.SlashMessenger`) på [nuget.org](https://www.nuget.org/packages/Fhi.Slash.Public.SlashMessenger).


## Innhold
- [SLASH Messenger (NuGet-pakke)](#slash-messenger-nuget-pakke)
  - [Innhold](#innhold)
  - [Struktur](#struktur)
      - [Mappestruktur](#mappestruktur)
      - [Klasseforklaring](#klasseforklaring)
  - [Hvordan implementere koden](#hvordan-implementere-koden)
      - [Sette opp prosjekt](#sette-opp-prosjekt)
      - [Eksempel og modifikasjoner](#eksempel-og-modifikasjoner)
      - [Konfigurasjoner](#konfigurasjoner)
  - [Logging](#logging)
  - [Tester](#tester)
      - [Struktur for integrasjonstester](#struktur-for-integrasjonstester)
  - [Gi oss dine tilbakemeldinger](#gi-oss-dine-tilbakemeldinger)

<div style="page-break-after: always"></div>

## Struktur
#### Mappestruktur
- **Extensions:** Utvidelser av ulike klasser og interfaces
  - Her finnes "ServiceCollectionExtensions" med `AddSlash`-metoden for å sette opp ulike ressurser for å håndtere HelseID og Slash
- **HelseID:** Kode relatert til kommunkasjon med HelseID og utveksling av AccessToken
- **Slash:** Kode relatert til kommunkasjon med Slash Mottak API
- **Tools:** Ulike støtteklasser for enkle operasjoner

#### Klasseforklaring
Filene under HelseID og Slash følger en felles struktur:

- **Interfaces** brukes for både client- og service-klasser, noe som gjør det mulig å bruke egne implementasjoner.
- **Client-klasser** håndterer kommunikasjonen mellom programmet og tjenester.
- **Service-klasser** inneholder forretningslogikk og benytter client-klassene for kommunikasjon.
- **Exception-klasser** fungerer som wrapper-exceptions som kastes av tilhørende klasser.
- **Default-klasser** er standardimplementasjoner av clients og services.
- **Tools-klasser** er støtteklasser som inneholder enkle, selvstendige metoder.
- **Extensions-klasser** er utvidelsesklasser som legger til ekstra logikk på eksisterende klasser.

## Hvordan implementere koden
For å ta i bruk denne koden anbefaler vi å laste ned NuGet-pakken. Dette gjør det enklere å håndtere oppdateringer.

#### Sette opp prosjekt
Opprett et .NET-prosjekt og lag en egen `ServiceCollection` for å legge til ressursene som pakken tilbyr. Dette gjøres ved å kalle metoden `AddSlash`, som finnes i klassen `ServiceCollectionExtensions`.

#### Eksempel og modifikasjoner
For et eksempel på bruk av NuGet-pakken og hvordan du kan gjøre egne tilpasninger til standardkoden, se dokumentasjonen for [SlashMessengerCLI](https://github.com/folkehelseinstituttet/Fhi.Slash.Mottak/tree/public-github/src/Slash.Public.APIMessengerCLI/README.md)

#### Konfigurasjoner
For å kjøre `AddSlash` kreves konfigurasjonsverdier for både HelseID og Slash:

- **HelseID-konfigurasjon**
Inneholder endepunkt for uthenting av AccessTokens og informasjon om HelseID-klientoppføringen.
Les mer om HelseID og autentisering [her](https://github.com/folkehelseinstituttet/Fhi.Slash.Mottak/blob/public-github/README.md#autentisering).

- **Slash-konfigurasjon**
Inkluderer endepunkt for innsending av meldinger og informasjon om EPJ-systemet som sender meldingene.

#### Maksimal størrelse på innsending

For å sikre stabil og effektiv håndtering av høy trafikk, bør hver http request som sendes inn være maksimalt 2 MB.
Vær oppmerksom på at dette ikke håndteres automatisk av denne pakken, så dere må selv implementere denne begrensningen i deres system.

## Logging
Standardimplementasjonen av services og clients inkluderer logging med to ulike nivåer:
 - **Trace:** Logger **start** og **slutt** for metodekall.
 - **Debug:** Logger **start** og **slutt** for metoder.

For å endre loggnivå for pakken, kan du bruke `appsettings.json`.
```
{
    ...
    "Logging": {
        "LogLevel": {
            ...
            "Fhi.Slash.Public.SlashMessenger": "Trace"
        }
    }
}
```

## Tester
Prosjektet inneholder både unit-tester og integrasjonstester, med eksempler som viser hvordan de ulike filene er strukturert.

Om en trenger testfiler så kan dette lastes ned fra vårt test miljø.
Se instruksjoner her: https://github.com/folkehelseinstituttet/Fhi.Slash.Public?tab=readme-ov-file#testfiler

#### Struktur for integrasjonstester
I mappen `TestFiles` finner du tre undermapper:
 - **Client:** Data for innsendingsprogrammet.
 - **HelseID:** Data for HelseID.
 - **Slash:**  Data for Slash Mottak API.

Denne strukturen sikrer at man får oversikt over hvilke datasett hver aktør benytter for at løsningen skal fungere korrekt.

<div style="page-break-after: always"></div>

## Gi oss dine tilbakemeldinger
Dersom du opplever problemer eller har forslag til forbedringer i dette repositoriet, vil vi sette stor pris på dine tilbakemeldinger. Din innsats bidrar til å gjøre prosjektet bedre for alle brukere.

Vi oppfordrer deg til å opprette et issue på GitHub dersom du finner feil, har spørsmål eller ønsker å foreslå nye funksjoner. Alle tilbakemeldinger, store som små, er verdifulle og hjelper oss med å forbedre kvaliteten og funksjonaliteten til løsningen.

Takk for at du bidrar til å gjøre prosjektet bedre!
[Opprett et issue på GitHub](https://github.com/folkehelseinstituttet/Fhi.Slash.Mottak/issues)