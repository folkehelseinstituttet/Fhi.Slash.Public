<!--
  To generate a PDF from this markdown file, use the extension "Markdown PDF" in Visual Studio Code.
  You may change the header and footer template in the settings of the extension.

  Current settings (Add these to settings.json directly):
  "markdown-pdf.headerTemplate": "<div></div>",
  "markdown-pdf.footerTemplate": "<div style=\"font-size: 9px; margin-left: auto; margin-right: 1cm; \"><span class='pageNumber'></span> / <span class='totalPages'></span></div>",
-->
<p align="right">
    <img src="docs/images/fhi-logo.svg" alt="drawing" width="100" style=""/>
</p>

# SLASH - API Mottak

Dette dokumentet tar for seg SLASH API-løsningen for å overføre helsedata til registeret.

Det er mulig å laste ned en fullstendig demoløsning for innsending.<br>
Se [Eksempelkode](#eksempelkode) for mer informasjon.

<br>

## Innhold

- [SLASH - API Mottak](#slash---api-mottak)
  - [Innhold](#innhold)
  - [Om tjenesten](#om-tjenesten)
    - [Krav](#krav)
  - [Overordnet flyt](#overordnet-flyt)
      - [Forklaring](#forklaring)
  - [NuGet-pakke og Slash Messenger](#nuget-pakke-og-slash-messenger)
  - [Eksempelkode og Slash Messenger CLI](#eksempelkode-og-slash-messenger-cli)
  - [Testfiler](#testfiler)
  - [Autentisering](#autentisering)
  - [API og Swagger](#api-og-swagger)
  - [Overføring av helsedata](#overføring-av-helsedata)
      - [1) Forbered meldingsinnhold og header-verdier](#1-forbered-meldingsinnhold-og-header-verdier)
        - [Header-verdier](#header-verdier)
      - [2) Hent public key og hash \& kryptere innhold](#2-hent-public-key-og-hash--kryptere-innhold)
      - [3) Hent Access Token fra HelseID](#3-hent-access-token-fra-helseid)
      - [3.1) Korrespondanse med HelseID](#31-korrespondanse-med-helseid)
      - [4) Send melding til API-tjenesten](#4-send-melding-til-api-tjenesten)
      - [5 a) Innlevering godkjent](#5-a-innlevering-godkjent)
      - [5 b) Innlevering feilet](#5-b-innlevering-feilet)
  - [Miljøer og API](#miljøer-og-api)
  - [Referanser](#referanser)

<div style="page-break-after: always"></div>

## Om tjenesten

Tjenesten er et REST-basert API designet for sikker overføring av helsedata. API-et tilbyr en moderne og effektiv løsning for å avlevere meldinger via HTTP i stedet for tradisjonelle EDI-baserte løsninger.

Meldinger sendes i JSON-format og må krypteres, signeres, og autentiseres ved bruk av HelseID. Ved mottak validerer tjenesten autentisering, signatur, header-verdier og meldingsstruktur i en sekvensiell prosess. Dette sikrer at avsenderen umiddelbart får tilbakemelding på om meldingen er teknisk godkjent og riktig mottatt.

### Krav

Tjenesten benytter HelseID til autentisering. Det stilles derfor krav til at enhver innsender har en oppføring i HelseID for den lokasjonen som skal sende inn data gjennom denne tjenesten. (HelseID-klienten bør være utstedt til bedriftsnummeret for den faktiske lokasjonen)

I tillegg til dette kan det være nødvendig å opprette nettverksåpninger mot API-tjenesten til FHI på NHN og helsenettet.

<div style="page-break-after: always"></div>

## Overordnet flyt

![Systemskisse](docs/images/innsending-flytskjema.jpg)
_Overordnet skisse på hvordan helsedata skal overføres med API-tjenesten_

#### Forklaring

1. EPJ genererer og formaterer et skjematisk strukturert JSON-objekt i henhold til den meldingstypen som skal avleveres.
   - Her lages også tilhørende header-verdier som må være en del av avleveringen.
2. Henter "Public Key" fra API-tjenesten som skal benyttes i kryptering.
   - Nøkkelen vil brukes i et hybrid krypteringssystem. Se [Referanser](#referanser) for mer informasjon.
3. Gjør klar meldingen for innsending
   1. Lag en hash av meldingen
   2. Symmetrisk kryptering av meldingen
   3. Asymmetrisk kryptering av nøkkelen med "Public Key" fra API-tjenesten
4. Access Token
   1. Lag et DPoP-bevis og gjør første forespørsel til HelseID for å få et Access Token
   2. Respons fra HelseID vil inneholde en "nonce"-verdi som skal brukes i neste steg
   3. Lag et nytt DPoP-bevis og gjør en ny forespørsel til HelseID for å få et Access Token
   4. Respons fra HelseID vil inneholde et Access Token med "cnf"-claim som inneholder informasjon om nøkkelpar som ble brukt i DPoP-beviset
5. Lag DPoP-bevis mot API-tjenesten
   - Eksempel på DPoP-bevis kommer under avsnittet [Avlevering av meldinger](#avlevering-av-meldinger)
6. Send melding til API-tjenesten
   1. Legg ved DPoP-beviset i HTTP-headeren "DPoP"
   2. Legg ved Access Token fra HelseID i HTTP-headeren "Authorization"
      - Authorization Scheme: DPoP
   3. Legg ved kryptert melding i HTTP-body
   4. Legg til nødvendige header-verdier
      - Se avsnittet [Avlevering av meldinger](#avlevering-av-meldinger)
7. (API-tjenesten) Validering av Access Token og DPoP-bevis
8. (API-tjenesten) Dekryptering og innholdsvalidering
   1. Validering av header-verdier
   2. Dekryptere meldingen og sjekke hash fra DPoP-beviset
   3. Validere meldingen mot meldingsskjema
9. (API-tjenesten) Lagring melding og send respons

Se [Referanser](#referanser) for mer informasjon om DPoP og HelseID.

<div style="page-break-after: always"></div>

## NuGet-pakke og Slash Messenger
For å forenkle integrasjonen med Slash Mottak API og effektivisere innsending av meldinger, er det utviklet en NuGet-pakke for bruk i .NET.
Kode for NuGet-pakken, samt dokumentasjon finner du her: [Slash Messenger](https://github.com/folkehelseinstituttet/Fhi.Slash.Public/tree/public-github/src/Fhi.Slash.Public.SlashMessenger)

NuGet-pakke for **Slash Messenger** finner du her: [nuget.org](https://www.nuget.org/packages/Fhi.Slash.Public.SlashMessenger)

## Eksempelkode og Slash Messenger CLI
Det finnes en testklient med kode for innsending av meldinger til API-tjenesten.
Du finner koden og dokumentasjon for testklienten her: [SlashMessengerCLI](https://github.com/folkehelseinstituttet/Fhi.Slash.Public/tree/public-github/src/Fhi.Slash.Public.SlashMessengerCLI)


## Testfiler

Testfiler for de ulike meldingstypene kan lastes ned via swagger.
1. Gå til swagger-siden, klikk på ønsket domene og så klikk på ønsket meldingstype i nedtrekkslisten oppe til høyre.
2. Trykk så på "Last ned eksempel på melding (json)" for å laste ned eksempel.

Swagger:
https://app-mottak-api-et.azurewebsites.net/swagger/index.html

Eksempel for HST_Konsultasjon:
https://app-mottak-api-et.azurewebsites.net/swagger-tools/download-example?messageType=HST_Konsultasjon&messageVersion=1


<div style="page-break-after: always"></div>

## Autentisering

API-tjenesten benytter HelseID til autentisering/tilgangssikring (machine to machine).
Dette realiseres ved hjelp av OpenID Connect(OIDC). Alle forespørsler til API-et blir dermed kontrollert mot HelseID.

For HelseID så kan en enten benytte klientkonfig fra rapporterende enhet direkte eller benytte klientkonfig fra EPJ (multi-tenant) for å få utstedt token (JWT) med nødvendige claims/påstander. Hvis en benytter multi-tenant så må EPJ sitt orgnummer settes som claim "helseid://claims/client/claims/orgnr_supplier", mens rapporterende enhet settes som "helseid://claims/client/claims/orgnr_parent".
Utstedt token skal deretter benyttes i forespørsler mot API-et.

Les mer om HelseID og den tekniske dokumentasjonen her:
[https://www.nhn.no/helseid/](https://www.nhn.no/helseid/)

<div style="page-break-after: always"></div>

## API og Swagger

API-tjenesten benytter Swagger for å dokumentere samtlige meldingstyper og versjoner.
Du finner Swagger-portalen her: [Swagger (ET - Eksternt Testmiljø)](https://app-mottak-api-et.azurewebsites.net/swagger/index.html)

Portalen viser endepunkter for API-et, samt meldingsstruktur og -skjema for de ulike meldingene mottaket har støtte for.

<div style="page-break-after: always"></div>

## Overføring av helsedata

Detaljert dokumentasjon på hvordan en skal overføre helsedata til API-tjenesten.

#### 1) Forbered meldingsinnhold og header-verdier

Meldinger skal sendes som JSON-format i HTTP-forspørsel (POST).
Sørg derfor for å strukturere meldingen i henhold til meldingsskjemaet for ønsket meldingstype.
NB! Når en skal sende meldinger så må content-type være "text/plain" og ikke "application/json" (ettersom at innholdet er kryptert)

##### Header-verdier

Følgende HTTP Header-verdier må legges til i forespørselen:

| Navn                      | Beskrivelse                                       | Påkrevd | Type              | Eksempel verdi       |
| ------------------------- | ------------------------------------------------- | ------- | ----------------- | -------------------- |
| x-vendor-name             | Navn på leverandør av software                    | Ja      | String            | _Softwarebedrift AS_ |
| x-software-name           | Navn på EPJ/programvare                           | Ja      | String            | _PasientJournal123_  |
| x-software-version        | Versjon på EPJ/Programvare                        | Ja      | String            | _1.0.4_              |
| x-export-software-version | Versjon på uttrekksmodul/-program                 | Ja      | String            | _3.0.9_              |
| x-data-extraction-date    | Dato for eksportering av data fra EPJ/programvare | Ja      | Date (dd.MM.yyyy) | _31.12.2023_         |

#### 2) Hent public key og hash & kryptere innhold

Uthenting av offentlig nøkkel, brukt til kryptering av symmetrisk nøkkel, gjøres med en HTTP-forespørsel mot API-tjenesten.

Endepunkt: `/keys`

Dette endepunktet returnerer en liste over gyldige nøkler. Bruk nøkkelen som har senest utløpsdato.
Nøkkelen kan caches og benyttes til kryptering av flere meldinger.

Eksempel på respons fra API:

```
[
    {
        "id": "AC39844BF1257C29C82A7845F9C0F3C936183104",
        "expirationDate": "9999-12-31T23:59:59.999",
        "publicKey": "-----BEGIN PUBLIC KEY-----\r\nMIIBojANBgkqhkiG9w0BAQEFAA ... -----END PUBLIC KEY-----"
    }
]
```

Verdien på `publicKey` skal benyttes ved kryptering av symmetrisk nøkkel. <br>
Verdien på `id` skal legges inn i DPoP-bevis mot API-tjenesten under claimet `enc_key_id`.

**Kryptering**

Fremgangsmåten for å kryptere innholdet er som følgende:

1. Lag en hash av meldingen
   - Hashen skal være en Base64-encoded SHA256 hash
1. Generer en symmetrisk nøkkel
   - Lengden på nøkkelen skal være 32 bytes eller mer
1. Krypter innholdet med den symmetriske nøkkelen.
1. Krypter den symmetriske nøkkelen med Public Key som tidligere ble hentet fra API-tjenesten.

Verdien for hash av melding skal legges inn i DPoP-bevis mot API-tjenesten under claimet `msh_hash`. <br>
Verdien for den krypterte symmetriske nøkkelen skal legges inn i DPoP-bevis mot API-tjenesten under claimet `enc_sym_key`.

#### 3) Hent Access Token fra HelseID

API-tjenesten benytter HelseID for autentisering og krever at et "Access Token" følger med forespørsel mot API-et.

Senere i prosessen skal det opprettes et DPoP-bevis som skal benyttes mot API-tjenesten.
Dette beviset vil inneholde en offentlig del av et nøkkelpar som innsender selv bestemmer.
Det er mulig å benytte nøkkelparet som ligger i HelseID-klientkonfigurasjonen, men man kan også benytte et eget nøkkelpar.

Uansett nøkkelpar som skal benyttes må dette paret "godkjennes" av HelseID.
Dette gjøres ved å lage et DPoP-bevis som er signert med ønsker nøkkelpar.
DPoP-beviset skal sendes sammen med en forespørsel til HelseID for å få et Access Token.

#### 3.1) Korrespondanse med HelseID

Lag et DPoP-bevis og legg dette i header-verdien "DPoP".

```json
{
  "alg": "RS256",
  "kid": "0-8E4RNYlw_aFRE-fVtclOLDgHtQB2KfzDIgTbE5X7g",
  "typ": "dpop+jwt",
  "jwk": {
    "kty": "RSA",
    "n": "nqI3e2qx30o08bw9mdW5CTxPaIyY3-UdYgF7Oa04P8wrEpcHIpWmPuYlNLLysWYEDPJa7SCRXL9X8pMHdF0t6b8Lago_Va4PFd0T8D6sWEmN3YH3KQNzxOQhqko3ojsQCGWhuM3zP47cfyk0PfoE4zYqUZZrYHiIgECbEjM0TRJCRrl6K7YEIWlnd5qVLFaXZiEEfITXAJrV6xaYHGducXzOlaqq89NsgmbGBUb4Kac8GU9tAuF6fdCUtLqfoTv0lsMnRoZHwTsSNrnrMjToRBdyDoQWIVydhTn5w61CMWnSksEXMShYDeTwTvrPOoKRncii6s2P3j9eEi9Okh6RbQ",
    "e": "AQAB",
    "alg": "RS256"
  }
}
.
{
  "jti": "3319f016-9760-413e-9529-06fd88de0093",
  "htm": "POST",
  "htu": "https://helseid-sts.test.nhn.no/connect/token",
  "iat": 1715356172
}
.
[Signature]
```

_Eksempel på decoded DPoP-bevis_

Lag en JWT med client assertion

```json
{
  "alg": "RS256",
  "typ": "JWT"
}
.
{
  "sub": "1185d326-422d-43e0-8c3c-4111d6fd4224",
  "iat": "1715356172",
  "jti": "00d0e27bc24947d6970a73fe1088863f",
  "nbf": 1715356172,
  "exp": 1715356232,
  "iss": "1185d326-422d-43e0-8c3c-4111d6fd4224",
  "aud": "https://helseid-sts.test.nhn.no/"
}
.
[Signature]
```

_Eksempel på decoded JWT_

Denne forespørselen skal sendes til HelseID sitt token-endepunkt.

Responsen på denne forespørselen vil feile, men inneholde en "nonce"-verdi som skal brukes i neste steg.

Lag så en ny JWT med client assertion og et nytt DPoP-bevis lik den første, men nå med claimet `nonce` med verdien fra første respons.

```json
{ ... }
.
{
  "jti": "22810a6a-2e31-41c2-a02c-7e0b20c80846",
  "htm": "POST",
  "htu": "https://helseid-sts.test.nhn.no/connect/token",
  "iat": 1715356808,
  "nonce": "CfDJ8GEx18wci25Io3ttUEmnMl5zGiV9JcByNBiwEKjwk9cKc0VQTfRRI19bIKyWL5AGEHuhLaCzejHU7I6x2IsM83fs--z5incMdwrfFDMWnt3TLRIZ-EYsgO0BtyEmcY3d3w"
}
.
[Signature]
```

Send forespørselen til HelseID sitt token-endepunkt.

Responsen vil nå inneholde et Access Token, som inneholder claimet `cnf` som beskriver nøkkelparet brukt i DPoP-bevisene.

Ved innsending til API-tjenesten skal Access Token benyttes som verdi for HTTP-headeren `Authorization` med syntaks `DPoP {accessToken}`.

#### 4) Send melding til API-tjenesten

Meldingen skal sendes som et HTTP POST-kall mot API-tjenesten. Det krypterte innholdet (payload) sendes i BASE64-format.
API-mottaket har følgende endepunkt: `/message`.

Lag et nytt DPoP-bevis for innsending til API-tjenesten

```json
{
  "alg": "RS256",
  "kid": "0-8E4RNYlw_aFRE-fVtclOLDgHtQB2KfzDIgTbE5X7g",
  "typ": "dpop+jwt",
  "jwk": {
    "kty": "RSA",
    "n": "nqI3e2qx30o08bw9mdW5CTxPaIyY3-UdYgF7Oa04P8wrEpcHIpWmPuYlNLLysWYEDPJa7SCRXL9X8pMHdF0t6b8Lago_Va4PFd0T8D6sWEmN3YH3KQNzxOQhqko3ojsQCGWhuM3zP47cfyk0PfoE4zYqUZZrYHiIgECbEjM0TRJCRrl6K7YEIWlnd5qVLFaXZiEEfITXAJrV6xaYHGducXzOlaqq89NsgmbGBUb4Kac8GU9tAuF6fdCUtLqfoTv0lsMnRoZHwTsSNrnrMjToRBdyDoQWIVydhTn5w61CMWnSksEXMShYDeTwTvrPOoKRncii6s2P3j9eEi9Okh6RbQ",
    "e": "AQAB",
    "alg": "RS256"
  }
}
.
{
  "jti": "5629c0cf-ef59-4058-b831-fbb573aad334",
  "htm": "POST",
  "htu": "https://app-mottak-api-et.azurewebsites.net/message",
  "iat": 1715426416,
  "msg_type": "HST_Konsultasjon",
  "msg_version": "1",
  "msg_hash": "XqOR1SZn1IchuHWWbWQ6rI1MWm4Bo0cmISenZZZLU1s",
  "enc_sym_key": "PTimgMeCZlKHik2kpkqrHa/RLoGwN7RDGm5Iee7VBKEygWxuzSNfQOUg7ybUHfjiVsv0uEEgiMtcXQeCZFlZfJtsad7ut9gZhHp699aPqR5J+Px5lgZJyyW1PPyE1uooyWcHMszxZV35WioRKDD0+cdUzZ0h6a8fiyJPxVhrcm/nhCHFBvkitbWaL2Ye0yiTc8t/H45RTjcZ9AOvW7awG8rtNPpOFF7nLZQ2BknliKvInLxRwCOBmBc2jfkaj2LiEW9+2gPzVEOTyRx6bKcRSXwLi7EUtwNT7NoqCs5RG2aG6eunZjg16i6HfZx8M4hg6I73yG9UI5oKcpRDEfFc9IsrDTBMnBGy0Cvjn6d0o8NqkRFNfsqbXWrVqhKKx9C62qTFRKvVbEXlzReUVK4YWMl/TOMQre6rBOjNqfOkoUEv51R8hpMNDbTJIxY0O55e3wtLIGRYNt4x0FzV3cmL2Dr314c2NNhI5zmR++Iy3+/r5lZWH0UkiYO+sORASw1U",
  "enc_key_id": "AC39844BF1257C29C82A7845F9C0F3C936183104",
  "ath": "_j4ZpplB6TBA-7EeIoHOnwqJf9XgItc1CXSti12ZD28"
}
.
[Signature]
```

_Eksempel på DPoP-bevis_

Bemerkninger:

- `typ` i header skal være `dpop+jwt`
- `jwk` i header skal korrespondere med cnf-claimet i Access Token
- `htm` i payload skal være HTTP-metoden som skal benyttes mot API-tjenesten
- `htu` i payload skal være URL-en til API-tjenesten for avlevering av meldinger
- `msg_type` i payload skal være meldingstypen som skal avleveres
- `msg_version` i payload skal være versjonen av meldingen som skal avleveres
- `msg_hash` i payload skal være hashen av meldingen før den ble kryptert
- `enc_sym_key` i payload skal være den krypterte symmetriske nøkkelen som ble brukt til å kryptere meldingen
- `enc_key_id` i payload skal være ID-en til offentlig nøkkel som ble brukt til å kryptere symmetrisk nøkkel
- `ath` i payload er hash av Access Token som blir brukt i forespørselen

Legg også til nødvendinge header-verdier som beskrevet i avsnittet [Forbered meldingsinnhold og header-verdier](#1-forbered-meldingsinnhold-og-header-verdier):

```
x-vendor-name: Softwarebedrift AS
x-software-name: PasientJournal123
x-software-version: 1.0.4
x-export-software-version: 3.0.9
x-data-extraction-date: 31.12.2023
```

#### 5 a) Innlevering godkjent

Dersom meldingen er godkjent vil responsen fra API-et ha responskode "HTTP 200 OK".<br>
Responesen vil inneha en `X-Correlation-ID` header med API-tjenestens interne id på forsendelsen.<br>
I tillegg vil en motta følgende respons i body:

```json
{
  "delivered": true,
  "errors": []
}
```

#### 5 b) Innlevering feilet

Dersom authentisering er ugyldig vil responsen fra API-et ha responskode "HTTP 401 Unauthorized".<br>
Dersom noe annet i forspørselen ikke validerer så vil responsen fra API-et ha responskode "HTTP 400 Bad Request".<br>
Responesen vil inneha en `X-Correlation-ID` header med API-tjenestens interne id på forsendelsen.<br>
I tillegg vil en motta ytterligere informasjon om hva som feilet i body. Eksempel:

```json
{
  "delivered": false,
  "errors": [
    {
      "errorCode": 2006,
      "propertyName": null,
      "errorMessage": "Error: SchemaValidationFailed | Schema validation failed",
      "errorDetails": "[{\"Location\":\"/0\",\"Errors\":[{\"Value\":\"Required properties [\\u0022orgNr\\u0022] are not present\"}]}]"
    }
  ]
}
```

**Oversikt over feilmeldinger**

| Feilkode | Feilmelding                                               | Beskrivelse                                                                 |
| -------- | --------------------------------------------------------- | --------------------------------------------------------------------------- |
| 1001     | HttpHeaderMissing                                         | Én eller flere manglende HTTP header-verdier                                |
| 1002     | HttpHeaderValidation                                      | Ugyldig verdi på én eller flere HTTP-headere                                |
| 1003     | InvalidMessageTypeVersion                                 | Ugyldig meldingstype og/eller meldingsversjon                               |
| 1004     | InvalidKeyId                                              | Ugyldig offentlig nøkkel                                                    |
| 1005     | InvalidDigest                                             | Ugyldig hash på innhold (payload)                                           |
| 1006     | PayloadHashMismatch                                       | Hash på innhold (payload) samsvarer ikke med header-verdi                   |
| 1007     | ExpiredKey                                                | Nøkkel som ble benyttet til kryptering av symmetrisk nøkkel er utgått       |
| 1008     | DecryptionErrorForAsymmetricalKey                         | Feil ved dekryptering av asymmetrisk nøkkel                                 |
| 1009     | DecryptionErrorForSymmetricalKey                          | Feil ved dekryptering av symmetrisk nøkkel                                  |
| 2001     | ShouldNotReceiveMessageForGivenOrganizationAndMessageType | API-tjenesten godtar ikke meldinger av gitt meldingstype fra organisasjonen |
| 2002     | MissingOrganizationNumberClaimFromHelseIdToken            | Access Token fra HelseID inneholder ikke organisasjonsnummer                |
| 2003     | MissingSignatureClaimFromHelseIdToken                     | Mangler signatur-claim i Access Token fra HelseID                           |
| 2004     | BadSignatureClaimFromHelseIdToken                         | Ugyldig format på signatur-claim i Access Token fra HelseID                 |
| 2005     | HelseIdSignatureDoesNotMatchHeaderValues                  | Signatur i Access Token fra HelseID samsvarer ikke med header-verdi         |
| 2006     | SchemaNotFound                                            | Fant ikke skjema som korresponderer med meldingstype og -versjon            |
| 2007     | InvalidJsonMessage                                        | Dekryptert melding er ikke Json-format                                      |
| 2008     | SchemaValidationFailed                                    | Dekryptert melding samsvarer ikke med meldingsskjema                        |
| 2009     | PayloadSizeLimitExceeded                                  | Payload size exceeded limit                                                 |
| 2010     | StorageError                                              | Failed to store the message                                                 |

<div style="page-break-after: always"></div>

## Feilhåndtering, maks størrelse, rekkefølge og køing av meldinger

For å sikre korrekt overføring av data må innsendinger skje sekvensielt og ha støtte for re-sending ved eventuelle feil/nettverksbrudd o.l.


**Rekkefølge og kø**

Innsending av meldinger skal skje i samme rekkefølge som dataene oppstod hos avsender.
- Integrasjonen bør implementere en kømekanisme for sending av meldinger (first in - first out).
- Dersom en melding feiler, må den bli liggende i køen til den er bekreftet levert (HTTP 200/201).
- Nye meldinger fra samme enhet skal ikke sendes før tidligere meldinger er bekreftet levert.

**Avvisning av meldinger (HTTP 4xx)**

En HTTP 4xx-feil betyr at innholdet eller strukturen i meldingen er feil, eller at innsendingen ikke kan aksepteres slik den er. I slike tilfeller skal innsending stoppes inntil feilen er rettet.

Det er viktig å merke seg at det ikke er tillatt å hoppe over en melding. Det skal aldri sendes inn meldinger med datoer/innhold som ligger senere enn den siste meldingen som ble godkjent levert. Hele serien må være komplett og uten hull.

Ved autentiseringsfeil og lignende (f.eks. 401/403) må avsender rette den tekniske årsaken (token, sertifikat e.l.) og forsøke samme innsending på nytt. Innholdet i meldingen trenger normalt ikke endres ved slike feil.

Er det feil på innhold eller struktur så bør en gjøre følgende:
- Loggføre feilen (HTTP-statuskode, feilmelding og X-Correlation-ID).
- Korrigere årsaken til feilen (teknisk feil, mapping, struktur, schema-validering osv.).
- Generere melding/meldinger på nytt fra siste godkjente periode.
- Sende korrigerte melding/meldinger på nytt.
- Når melding/meldinger er bekreftet levert (HTTP 200/201), kan innsending av senere meldinger fortsette.

***Maks størrelse av melding***

Meldinger med payload som overgår 15 megabyte i størrelse blir avvist med feilkode 2009.

**Tekniske feil (HTTP 5xx eller nettverksfeil)**

Ved midlertidige tekniske feil eller nettverksfeil bør systemet ha støtte for å forsøke å sende meldingen på nytt etter en viss tid. Det bør samtidig settes et maksimalt antall forsøk, og systemet bør varsle dersom feilen vedvarer. Meldingen skal først anses som levert når API-et returnerer HTTP 200/201.


### Initial last / større innsendinger  

Ved oppstart eller når man har en større mengde historiske data for innsending til API-tjenesten, anbefales følgende prosess:

#### 1. Oppdeling i batcher  
- For å unngå tekniske begrensninger anbefales det å dele opp data i mindre forsendelser. Se også maks størrelse ovenfor.  
- En praktisk oppdeling er måned-for-måned eller annen hensiktsmessig logisk inndeling som holder seg godt under grensen.

#### 2. Sekvens og kvittering  
- Batchene må sendes i kronologisk rekkefølge (eldste data først). 
- En må vente på **positiv kvittering (OK)** før en fortsetter neste batch.

#### 3. Løpende innsending etter initial last  
- Når initial last er gjennomført, kan leverandøren sende løpende meldinger som normal drift. Anbefalt er daglig innsending.
- Rekkefølge og kvittering gjelder også her: Opprettelser/oppdateringer må sendes i riktig rekkefølge og vent gjerne på kvittering før neste innsending.


<div style="page-break-after: always"></div>

## Miljøer og API

- ET (Eksternt Testmiljø): https://app-mottak-api-et.azurewebsites.net/ (Azure) ([Link til Swagger](https://app-mottak-api-et.azurewebsites.net/swagger/index.html))
- QA/Pilot: https://apimottak01.qa.fihr.no/ (Helsenett) ([Link til Swagger](https://apimottak01.qa.fihr.no/swagger/index.html))
- Prod: https://apimottak01.fihr.no/ (Helsenett) ([Link til Swagger](https://apimottak01.fihr.no/swagger/index.html))

<div style="page-break-after: always"></div>

## Referanser
- [Hybrid Krypteringssystem](https://en.wikipedia.org/wiki/Hybrid_cryptosystem)
- [NHN DPoP](https://www.nhn.no/tjenester/helseid/sikkerhetskonsepter-i-kontekst-av-helseid/dpop-for-produkteiere-og-arkitekter)
- [NHN HelseID](https://www.nhn.no/helseid/)
