# Continental

Juego de cartas Continental para Android, Windows y navegador. **Quien crea la sala hace
de servidor**: levanta un servidor web en su propio dispositivo, los demás se unen desde la
app o, si no la tienen, abriendo una dirección en el navegador.

## Por qué está montado así

Lo obvio sería meter ASP.NET Core dentro de la app MAUI y servir Blazor Server desde el
móvil. **No se puede**: `Microsoft.AspNetCore.App` no tiene runtime pack para
`net10.0-android` ni `net10.0-ios`, y la petición oficial
([dotnet/aspnetcore#35077](https://github.com/dotnet/aspnetcore/issues/35077)) lleva en
*Backlog* desde 2021.

La solución es **GenHTTP**: un servidor web en C# puro, con motor interno sin dependencia de
Kestrel, que sí corre en Android. Sobre él van los WebSockets del juego y el cliente
WebAssembly que se sirve a los navegadores.

## Proyectos

| Proyecto | Qué es | TFM |
|---|---|---|
| `Continental.Core` | Motor de juego: cartas, reglas, combinaciones, bots, protocolo. Sin UI ni red. | `net10.0` |
| `Continental.Shared` | Biblioteca Razor con **toda** la interfaz. La usan la app y el navegador. | `net10.0` |
| `Continental.Server` | Servidor embebido: WebSockets, estáticos y anuncio UDP. Sin dependencias de MAUI. | `net10.0` |
| `Continental.WebClient` | Blazor WebAssembly. Se empaqueta dentro de la app y lo sirve el anfitrión. | `net10.0` |
| `Continental` | App MAUI Blazor Hybrid. Aloja la sala y también se une a otras. | `android` / `windows` |
| `Continental.Core.Tests` | Pruebas del motor y de red real (servidor y WebSocket de verdad). | `net10.0` |

### La pieza clave: `IGameClient`

Los componentes nunca hablan con el motor directamente:

- **Anfitrión** → `LocalGameClient`: llamadas en proceso. No espera a la red para jugar.
- **Invitado con app** → `RemoteGameClient`: WebSocket.
- **Invitado por navegador** → el mismo `RemoteGameClient`, compilado a WebAssembly.

Por eso la UI es una sola. Y por eso **añadir juego por internet más adelante es una clase
nueva**, sin tocar el motor ni las pantallas.

## Compilar y ejecutar

```bash
# Pruebas del motor
dotnet test Continental.Core.Tests

# App de escritorio (la forma más rápida de probar)
dotnet build Continental -f net10.0-windows10.0.19041.0
dotnet run   --project Continental -f net10.0-windows10.0.19041.0

# Android
dotnet build Continental -f net10.0-android

# Sin reempaquetar el cliente web (compila mucho más rápido)
dotnet build Continental -f net10.0-android -p:BuildWebClient=false
```

El target `BundleWebClient` publica `Continental.WebClient`, lo comprime y lo mete en
`Resources/Raw/webclient.zip` (~5,5 MB). Solo se reejecuta cuando cambian las fuentes de la
UI compartida o del cliente. La app lo descomprime la primera vez que levanta una sala.

## Distribuir

```powershell
build\publish.ps1            # APK + Windows
build\publish.ps1 -Target android
build\publish.ps1 -Target android -AllAbis   # añade ARM de 32 bits
```

Deja en `dist\`:

| Fichero | Qué es |
|---|---|
| `Continental.apk` | ~25 MB, `arm64-v8a`, minSdk 24 (Android 7). Firmado para instalar fuera de Play. |
| `Continental-Windows\` | Carpeta autocontenida (~244 MB). `Continental.exe` arranca sin instalar .NET ni el Windows App SDK. |
| `Continental-Windows.zip` | Lo mismo comprimido (~93 MB), para copiar a otro equipo. |

### La clave de firma

El APK se firma con `build\continental.keystore` (alias `continental`). **Ni el fichero ni
la contraseña se versionan**: el `.gitignore` excluye `*.keystore` y la contraseña se lee de
`CONTINENTAL_KEYSTORE_PASSWORD`. En CI ambos viajan como secretos del repositorio.

```powershell
$env:CONTINENTAL_KEYSTORE_PASSWORD = '...'
build\publish.ps1
```

Quien tenga ese keystore puede firmar actualizaciones que tus móviles aceptarán como
legítimas, así que trátalo como una credencial: copia de seguridad fuera del repositorio.

**Guarda ese keystore.** Android se niega a instalar una actualización firmada con una clave
distinta, así que perderlo obliga a desinstalar la app en cada móvil antes de poder poner la
siguiente versión.

Dos detalles que cuestan tiempo si no se saben:

- **Firmar es un target incremental.** Si queda un APK firmado de antes, MSBuild da la salida
  por buena y conserva la firma vieja en silencio: seguirías publicando con el certificado de
  depuración sin enterarte. Por eso el script borra `bin\Release
et10.0-android` antes.
- **Windows necesita `-p:UseMonoRuntime=false`.** Sin eso el publish autocontenido busca un
  runtime pack de Mono para `win-x64` que no existe, y falla con NU1102.

### Instalar en el móvil

Pasa el APK por cable, Telegram o lo que sea, ábrelo y permite "instalar apps desconocidas"
cuando lo pida (en MIUI está más escondido que en One UI). Ambos móviles objetivo son
`arm64-v8a`, así que el APK les sirve tal cual.

#### Si el móvil dice "La aplicación no está instalada"

Casi siempre es esto:

```
INSTALL_FAILED_UPDATE_INCOMPATIBLE: Existing package com.companyname.continental
signatures do not match newer version
```

Ya hay una copia instalada firmada con **otra clave**, normalmente un despliegue de
depuración hecho desde Visual Studio. Android jamás reemplaza una app por otra con firma
distinta, y One UI lo resume con ese mensaje que no explica nada. **Desinstala la app y
vuelve a instalar el APK.**

Como consecuencia, desplegar en depuración y usar el APK de publicación son mutuamente
excluyentes: cada cambio de uno a otro exige desinstalar. Se evitaría haciendo que el build
de depuración firmara con `build\continental.keystore`, pero entonces la clave de
publicación circularía en el día a día.

Para ver el error real en vez del mensaje genérico del móvil:

```powershell
adb install -r dist\Continental.apk
```

## Cómo se juega en red

1. Alguien crea una sala. La app abre el puerto **8080** (o el siguiente libre) y empieza a
   anunciarse por **UDP broadcast** en el 45678.
2. Las demás apps del mismo WiFi ven la sala en la lista, sin escribir nada.
3. Quien no tenga la app abre `http://<ip-del-anfitrión>:8080` y recibe el cliente
   WebAssembly. Como la página ya viene del anfitrión, la dirección aparece rellenada sola.
4. Todo el mundo, app o navegador, habla el mismo protocolo por `/ws`.

## Sonido

Todo el audio se **sintetiza con la Web Audio API** (`Continental.Shared/wwwroot/continental-audio.js`);
no hay ni un fichero de sonido. El invitado se descarga el cliente por WiFi, así que el
diseño sonoro completo cuesta **0 bytes**, y generarlo permite variar el tono de cada carta
para que repartir trece no suene a una muestra disparada trece veces.

Dos familias, deliberadamente separadas:

- **Cartas = ruido.** Ráfagas filtradas con envolventes muy rápidas: suenan a papel y fieltro.
- **Lo que exige atención = tonos.** Solo tu turno, bajarte, robar de contra y el final de
  partida. Así destacan sin subir el volumen.

Los sonidos se disparan desde los **cambios de estado**, no desde tus pulsaciones, así que
el turno de un bot o la jugada de otro se oyen igual. El botón 🔊 silencia y la preferencia
se guarda en `localStorage`. Los navegadores bloquean el audio hasta que hay un gesto real,
por eso se desbloquea al entrar en la sala.

## Movimiento de cartas

Cuando una carta cambia de sitio el DOM simplemente se actualiza: un fotograma está aquí y
al siguiente allí, que se lee como teletransporte. En vez de eso vuela una **copia fantasma**
por encima de todo (`continental-fly.js`) mientras la original espera oculta.

Lo que hace que se vea natural y no mecánico:

- **Arco, no línea recta.** Una carta deslizada sobre el fieltro nunca va recta; el punto
  medio se eleva `min(46px, distancia × 0.16)`.
- **Giro que se asienta en cero**, para que aterrice plana en vez de encajar de golpe.
- **Sale rápido y llega despacio**, que es como se comporta algo lanzado.
- **Volteo** para las que salen del mazo: enseñan el dorso y se giran justo antes de llegar.

Se dispara desde los cambios de estado, así que el descarte de un bot se ve igual que el tuyo.

## Trampas resueltas (que no son obvias)

- **`OverrideHtmlAssetPlaceholders` es obligatorio** en `Continental.WebClient`. Sin él el SDK
  no reescribe los marcadores de `index.html`, el runtime se publica sin huellas, **no se
  genera `dotnet.boot.js`** y el navegador se queda colgado en la pantalla de carga para
  siempre. El `index.html` debe conservar `<link rel="preload" id="webassembly">`,
  `<script type="importmap">` y `blazor.webassembly#[.{fingerprint}].js`.
- **Nada complejo cruza el interop de JS.** Pasar un `record` a JavaScript depende de
  serialización por reflexión, justo lo que el trimming elimina en silencio en una
  compilación publicada de WebAssembly. Los vuelos con selector (una cadena) funcionaban y
  los que partían de un rectángulo no hacían nada, sin error. Todo lo que cruza es ahora
  una cadena o un número.
- **GenHTTP no conoce `application/wasm`** y etiqueta los ensamblados como
  `application/force-download`, que el navegador no compila en streaming. Reescribir la
  cabecera después no sirve; `WebAssemblyContentTypes` sirve esas extensiones él mismo con
  el tipo correcto (y bloquea el path traversal, porque escucha en la red local).

## Límites conocidos

- **Solo LAN.** Sin NAT traversal no hay partidas por internet. Dos móviles con datos
  móviles no se ven. Es inherente a "el que crea la sala es el servidor".
- **El navegador no oye el UDP**, así que el invitado web necesita la dirección (por eso se
  muestra en grande en el vestíbulo).
- **Si el anfitrión se va, la partida muere.** No hay migración de anfitrión.
- **HTTP en claro.** En un WiFi doméstico no hay autoridad certificadora; nada sale de la
  red local. Android lo permite vía `usesCleartextTraffic`.
- **iOS y macOS no están compilados.** El código está preparado, pero hace falta un Mac.

## Reglas

Las variantes regionales no coinciden, así que el anfitrión elige y **cualquiera puede ver
las reglas activas** desde el vestíbulo o durante la partida.

| | España | Latinoamérica |
|---|---|---|
| Cartas en la 1ª ronda | 6 | 7 |
| Valor del As | 20 | 30 |
| As en escalera | alto, bajo y bisagra (K-A-2) | solo alto |
| Trío con palos distintos | sí | no |

Comunes a ambas: 2 barajas + 6 comodines, comodín 50 pts, figuras 10 pts, las siete rondas
`TT → TE → EE → TTT → TTE → TEE → EEE`, robar de contra con carta de castigo, y −10 por
bajarse y cerrar en la misma jugada.

Todo lo anterior es configurable en `GameOptions`; los dos presets son puntos de partida.
