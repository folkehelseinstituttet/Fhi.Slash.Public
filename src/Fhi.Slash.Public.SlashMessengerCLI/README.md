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

# SLASH Messenger CLI

Dette dokumentet er en veiledning for bruk av SlashMessenger-pakken i NuGet. Det gir en oversikt over eksempelkoden og forklarer hvordan du kan ta den i bruk.

<br>

## Innhold

- [SLASH Messenger CLI](#slash-messenger-cli)
  - [Innhold](#innhold)
  - [Om eksempelkode](#om-eksempelkode)
  - [Overordnet flyt](#overordnet-flyt)
  - [Egne utvidelser](#egne-utvidelser)
    - [Tjenester og klienter](#tjenester-og-klienter)
    - [HttpClient](#httpclient)
    - [Endre JWK i DPoP-bevis](#endre-jwk-i-dpop-bevis)
      - [Slik gjør du det](#slik-gjør-du-det)
    - [Eksempler](#eksempler)
  - [Gi oss dine tilbakemeldinger](#gi-oss-dine-tilbakemeldinger)

<div style="page-break-after: always"></div>

## Om eksempelkode

Eksempelkoden i dette prosjektet er skrevet i .Net 8 (C#) og benytter seg av NuGet-pakken `Fhi.Slash.Public.SlashMessenger` som ligger ute på [nuget.org](https://www.nuget.org/packages/Fhi.Slash.Public.SlashMessenger).

Konfigurasjoner av de ulike verdiene kan gjøres i `appsettings.json` (eller `appsettings.Development.json` ved lokal testing).
Her har du mulighet til å endre informasjon om innsender, som for eksempel navn på EPJ-system og versjoner.
Oppsett av HelseID og konfigurasjoner av HelseID-klient skal også inn i disse filene.

For å kunne teste en innsending kreves en oppføring i HelseID. Mer om dette kan du finne [her](https://github.com/folkehelseinstituttet/Fhi.Slash.Mottak?tab=readme-ov-file#autentisering).

Det er implementert logging av operasjoner som skjer i nuget. For å se detaljer ved kjøring av programmet sett `LogLevel` for `Fhi.Slash.Public.SlashMessenger` til `Trace`.

HelseID anbefaler å hente `TokenEndpoint` fra `DiscoveryDocument` i stedet for å hardkode endepunktet. Derfor demonstrerer eksempelkoden hvordan token-endepunktet kan hentes dynamisk ved hjelp av dette dokumentet.

Om en trenger testfiler så kan dette lastes ned fra vårt test miljø.
Se instruksjoner her: https://github.com/folkehelseinstituttet/Fhi.Slash.Public?tab=readme-ov-file#testfiler

For å klargjøre programmet må prosjektkoden lastes ned og bygges.
Etter bygging får man filen `Fhi.Slash.Public.SlashMessengerCLI.exe`, som kan kjøres direkte.

Input-argumenter:

- Full filsti til meldingen som skal sendes (JSON-fil).
- Meldingstype (f.eks. 'HST_Konsultasjon').
- Meldingsversjon (f.eks. 1).
- Uttreksdato (f.eks. 01.01.2024). Denne verdien er valgfri; hvis den ikke settes, benyttes dagens dato.

Eksempel:

`> C:/folder1/Fhi.Slash.Public.SlashMessengerCLI.exe "C:/folder2/message.json" "HST_Konsultasjon" "1" "01.01.2024"`

## Overordnet flyt

For en detaljert forklaring av innsending av data til Slash, besøk GitHub-siden for Slash-prosjektet: [Fhi.Slash.Mottak](https://github.com/folkehelseinstituttet/Fhi.Slash.Mottak?tab=readme-ov-file#overordnet-flyt)

<div style="page-break-after: always"></div>

## Egne utvidelser

For å sette opp integrasjon mot **Slash** og **HelseID** kan du bruke `AddSlash`-metoden fra `Fhi.Slash.Public.SlashMessenger.Extensions` som finnes i NuGet-pakken `Fhi.Slash.Public.SlashMessenger`.

Når `AddSlash`-metoden kalles, legges flere tjenester, klienter og `HttpClient`-instanser til automatisk. Disse kan overstyres ved behov.

#### Tjenester og klienter

`AddSlash` forsøker å registrere nødvendige tjenester og klienter i `ServiceCollection`. Dersom implementasjoner av de aktuelle interfacene allerede finnes, vil ikke `AddSlash` erstatte disse.  
Hvis du ønsker å bruke egne implementasjoner, bør disse registreres **før** du kaller `AddSlash`-metoden.

#### HttpClient

Egendefinerte implementasjoner av `HttpClient`-instanser fungerer på samme måte.
Dersom du ønsker å bruke spesialtilpassede `HttpClient`-implementasjoner, må disse legges til **før** du kaller `AddSlash`-metoden.<br/>
Det er viktig at navnet på `HttpClient`-instansen stemmer overens med det tilsvarende navnet i konfigurasjonsfilene. Se standardverdiene i `SlashConfig.cs` og `HelseIdConfig.cs` for egenskaper med suffikset `ClientName`.

Standardverdiene for `HttpClient`-navn kan erstattes ved behov som en del av konfigurasjonen i `AddSlash`-metoden.

<div style="page-break-after: always"></div>

#### Endre JWK i DPoP-bevis

I standardimplementasjonen brukes samme **JWK** (JSON Web Key) for å signere både **ClientAssertion**-forespørsler mot **HelseID** og **DPoP-bevis** mot **Slash**.

Dersom du ønsker å benytte en annen JWK for signering av **DPoP-bevis** enn den som brukes mot **HelseID**, kan dette enkelt konfigureres. For å oppnå dette må du legge til en egen **KeyedSingleton** før du kaller `AddSlash`.

##### Slik gjør du det

1. **Nøkkelen** som skal brukes for å registrere **KeyedSingleton** er gitt i `dPoPProofJwkKey`, som finnes i klassen `ServiceCollectionExtensions`.
2. Verdien for **KeyedSingleton** skal være en instans av `JsonWebKey`.

#### Eksempler

```csharp
// Registrer en egen JWK for signering av DPoP-bevis
services.AddKeyedSingleton(ServiceCollectionExtensions.dPoPProofJwkKey, new JsonWebKey { ... });

// Registrer egne implementasjoner for tjenester
services.AddTransient<ISlashService, MyCustomSlashService>();
services.AddTransient<IHelseIdClient, MyCustomHelseIdClient>();

// Overstyr standard HttpClient
var slashBasicClientName = "MyBasicClient";
services.AddHttpClient(slashBasicClientName, config => config.BaseAddress = new Uri("https://my-domain.com"));

// Konfigurer og legg til Slash-integrasjon med tilpasset oppsett
services.AddSlash(slashConfig => {
    slashConfig.BasicClientName = slashBasicClientName; // Erstatter standardnavn på HttpClient
    ...
}, helseIdConfig => { ... });
```

Dette gir deg fleksibilitet til å tilpasse integrasjonen etter dine behov, enten ved å bruke standardimplementasjonene eller dine egne tilpasninger.

<div style="page-break-after: always"></div>

## Gi oss dine tilbakemeldinger

Dersom du opplever problemer eller har forslag til forbedringer i dette repositoriet, vil vi sette stor pris på dine tilbakemeldinger. Din innsats bidrar til å gjøre prosjektet bedre for alle brukere.

Vi oppfordrer deg til å opprette et issue på GitHub dersom du finner feil, har spørsmål eller ønsker å foreslå nye funksjoner. Alle tilbakemeldinger, store som små, er verdifulle og hjelper oss med å forbedre kvaliteten og funksjonaliteten til løsningen.

Takk for at du bidrar til å gjøre prosjektet bedre!
[Opprett et issue på GitHub](https://github.com/folkehelseinstituttet/Fhi.Slash.Mottak/issues)
