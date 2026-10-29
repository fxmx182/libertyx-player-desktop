# LibertyX Player — Windows e Linux

**Windows:** [instalador (Windows 10/11)](https://github.com/fxmx182/libertyx-player-desktop/releases/download/windows-latest/LibertyXPlayer-Setup.exe)
· [ARM64 (Snapdragon)](https://github.com/fxmx182/libertyx-player-desktop/releases/download/windows-latest/LibertyXPlayer-Setup-arm64.exe)
· [portátil e todos os arquivos](https://github.com/fxmx182/libertyx-player-desktop/releases/tag/windows-latest)

**Linux:** [.deb (Debian, Ubuntu, Mint)](https://github.com/fxmx182/libertyx-player-desktop/releases/latest/download/libertyx-player_amd64.deb)
· [.rpm (Fedora, openSUSE)](https://github.com/fxmx182/libertyx-player-desktop/releases/latest/download/libertyx-player.x86_64.rpm)
· [.tar.gz](https://github.com/fxmx182/libertyx-player-desktop/releases/latest/download/LibertyXPlayer-linux.tar.gz)
· [ARM64 e todos os arquivos](https://github.com/fxmx182/libertyx-player-desktop/releases/latest)

O mesmo player do Android e do iPhone, no desktop. Mesmo nome, mesma biblioteca com capas e
"continuar assistindo", mesma rolagem quadro a quadro, mesmas pastas da rede (SMB) — e o mesmo
LibertyX Pro, com a mesma chave.

## A escolha do motor: mpv, não VLC

Os irmãos usam o libVLC. Aqui o motor é o **libmpv**, por duas razões que só aparecem no desktop:

| | libVLC no desktop | libmpv |
|---|---|---|
| **Controles por cima do vídeo** | o vídeo é uma janela nativa enfiada na do app; nada desenha por cima dela sem truque de janela transparente (que depende do compositor) | desenha por OpenGL dentro de um controle comum do Avalonia: barra, balão e painéis ficam por cima, como em qualquer tela |
| **Rolagem quadro a quadro** | `setTime` exato, mas lento em arquivo grande | `hr-seek` é o caso de uso principal do mpv; buscas acumuladas durante o arraste são fundidas |
| **SMB** | tem `smb://`, mas a senha na URL é descartada (a armadilha do Android) | o app entrega os bytes pelo `stream_cb` do libmpv: `lxsmb://`, com o SMBLibrary (C# puro), sem senha no endereço |

No Linux o libmpv é o **da distribuição** (dependência do .deb/.rpm): o app não leva FFmpeg nem mpv
dentro. Isso deixa o pacote pequeno, recebe as correções de segurança do sistema e não redistribui
código GPL junto com o app.

## Windows e Linux no mesmo código

Uma base só, como no Partition: tudo que é de um sistema fica atrás de `OperatingSystem.IsWindows()`
(o pouco de API do Windows está em `Core/Win.cs`) e **mudança feita para um sistema nunca altera o
outro**. Cada um tem seu CI e seu release:

| | Windows | Linux |
|---|---|---|
| CI | `.github/workflows/windows.yml` (x64 e ARM64, Windows de verdade) | `.github/workflows/linux.yml` |
| Release | `windows-latest` (fixo, atualizado por cima) | `v1.0.N-…`, marcado latest |
| Sai versão quando o commit começa com | `Windows:` ou `Windows e Linux:` | `Linux:` ou `Windows e Linux:` |

Commit sem um desses prefixos (CI, testes, documentação) compila e testa os dois, e nenhuma versão sai.

O que muda no Windows:

- **O libmpv vai junto** (`libmpv-2.dll` ao lado do .exe): no Windows não há mpv do sistema, e as
  builds prontas saem em GPL. `Scripts/libmpv-windows.sh` compila do Linux, com o llvm-mingw, o mpv
  com `-Dgpl=false` e o FFmpeg sem `--enable-gpl` (mais libplacebo, libass, dav1d, FreeType,
  HarfBuzz, FriBidi, zlib — todos LGPL ou mais livres), estáticos dentro de uma DLL que só depende
  do que o Windows já tem. O CI guarda a DLL em cache enquanto o script não mudar. Sem Lua: os
  sites de vídeo pelo `yt-dlp` ficam só no Linux.
- **Vídeo**: o Avalonia desenha por ANGLE (OpenGL ES sobre Direct3D 11) e o mpv desenha dentro dele.
  O ANGLE entrega cada quadro numa textura compartilhada do Direct3D; onde o driver não compartilha
  (Wine, alguns drivers, área de trabalho remota) o Avalonia quebrava no meio do desenho e a janela
  congelava. Por isso o app cria antes uma textura igual (`Win.TexturaCompartilhada`) e, sem
  compartilhamento, já começa pelo vídeo por software. `LIBERTYX_GL=wgl` troca pelo OpenGL do driver.
- **Atualização ao abrir** (`Core/Atualizacao.cs`, como no Partition): o app lê o `versao.txt` do
  release e, havendo build maior, pergunta — sem cortar um vídeo que está tocando. Instalado: baixa
  o instalador, confere o tamanho e roda `/SILENT /UPDATE=1`, que troca a versão e reabre o app.
  Portátil (zip) e Linux: abre a página da versão nova. `LIBERTYX_BUILD=0` finge um build velho
  para testar; `LIBERTYX_SEM_ATUALIZACAO=1` desliga.
- **Biblioteca**: Vídeos e Downloads do usuário (pastas conhecidas, mesmo se mudadas de lugar) e os
  outros drives (D:, E:, pendrives) — o C: inteiro não, que é o sistema.
- **Rede**: sem avahi; a varredura da porta 445 acha os servidores e o nome vem pelo NetBIOS/LLMNR.
  Adaptadores virtuais (Hyper-V, WSL, VirtualBox, VMware) ficam de fora.
- **Tela acesa** com `SetThreadExecutionState`; **escala** da interface vem do próprio Windows.
- **Instalador** Inno Setup (`packaging/windows/LibertyXPlayer.iss`): instala sem administrador (ou
  para todos, se a pessoa escolher), em inglês/português/espanhol, e registra o app no "Abrir com" de
  cada tipo de vídeo e em Aplicativos padrão — o Windows 10/11 não deixa app nenhum se fazer padrão
  sozinho. O CI instala em silêncio, confere, abre o app instalado, desinstala e confere que nada
  sobrou. Artes e ícone: `Scripts/instalador.py`.
- Dados em `%APPDATA%\LibertyX Player` e `%LOCALAPPDATA%\LibertyX Player\Cache` (diário, miniaturas).
- Teste local sem Windows: o .exe roda no **Wine** (autoteste e janela; o ANGLE do Wine não
  compartilha textura, então ali o vídeo sai sempre pelo caminho por software). O instalador do
  Inno é de 32 bits: o Wine precisa do `wine32` e de um prefixo criado com ele.

## O que tem

- **Biblioteca**: Vídeos, Downloads, pendrives e HDs montados (`/media`, `/run/media`) e as pastas
  que a pessoa acrescentar. Pastas com capa (o vídeo mais novo), grade ou lista, busca, ordem por
  nome/data/tamanho, ocultar pasta. O Linux não tem um MediaStore: o app varre (8 níveis, ignora
  pastas ocultas e `.nomedia`).
- **Continuar assistindo** e "continuar de onde parou?" ao reabrir — como no celular.
- **Player**: barra com quadro exato debaixo do mouse, faixas de áudio e legendas (com nome do
  idioma), legenda externa (botão ou arrastar o `.srt` para a janela), atraso de áudio/legenda,
  velocidade, proporção e zoom, modo noturno, aleatório, repetir, próximo/anterior na pasta,
  tela cheia, informações do vídeo.
- **Abrir com**: o `.desktop` registra os tipos de vídeo; um arquivo aberto assim toca com a pasta
  dele como fila. Endereço http/https/rtsp/m3u8 também (e sites de vídeo, se houver `yt-dlp`).
- **Rede (Pro)**: servidores salvos, busca automática (anúncio mDNS pelo avahi + varredura da porta
  445 nas redes do computador), compartilhamentos, pastas, miniaturas e reprodução direto do servidor.
- **Idiomas**: português, inglês (padrão de qualquer outro) e espanhol; escolha em Ajustes.
- **Tela 4K**: a mesma detecção de escala do Partition Linux (variáveis do GTK/Qt, `Xft.dpi`,
  tamanho físico do monitor), com escolha manual em Ajustes.

## Mouse e teclado

Os gestos do MX Player viram mouse:

| | |
|---|---|
| Clique / Espaço | tocar / pausar |
| Clique duplo / F | tela cheia |
| Arrastar o vídeo ↔ | rolar com o quadro exato (largura da tela = 2 min) |
| Arrastar ↕ à direita / à esquerda | volume / brilho |
| Segurar o clique | acelera enquanto segura (2×, ajustável) |
| Roda / ↑ ↓ | volume (até 150%) · Ctrl + roda: zoom |
| ← → · Shift + ← → | ±10 s (ajustável) · ±1 min |
| `,` `.` | quadro anterior / próximo |
| `[` `]` ⌫ | velocidade − / + / normal |
| A · V | próxima faixa de áudio · legenda |
| N · P | próximo · anterior |
| S · D · M | captura · modo noturno · sem som |
| 0–9 | ir para 0%…90% |
| Botão direito | menu de opções |

## LibertyX Pro

Igual ao celular: o player inteiro é grátis; uma compra única (**US$ 5**) libera **pastas da rede,
mini janela (sempre por cima, no canto da tela), timer para dormir e captura de quadro**. Os sete
primeiros dias vêm liberados, contados pelo app. Terminado o teste, só o Pro trava.

A compra é a **chave LibertyX** do bot do Telegram (`@libertx_partition_bot`, Bitcoin/USDT), o
mesmo `LXP1-…` de produto **2** do Android — quem já comprou o Player para a TV já tem o do PC — e
o pacote (9) também vale. A chave é conferida a cada abertura (`Core/Pro.cs`).

| Build | Pro |
|---|---|
| `dotnet build`/`publish` nesta máquina (sem a variável `CI`) | liberado — é o do dono |
| o mesmo com `-p:LibertyxLoja=true` | travado, para testar a tela do Pro |
| o do CI (release do GitHub) | travado |

## Compilar e testar

```bash
cd src/LibertyX.Player && dotnet build -r linux-x64 --no-self-contained
```

`-r linux-x64` importa: sem ele a ICU embutida não entra e o app fecha em distribuição sem libicu.
Precisa do `libmpv2` instalado para rodar.

Windows, daqui mesmo do Linux:

```bash
Scripts/libmpv-windows.sh x64 dist/win-x64     # ~10 min na primeira vez; precisa de meson, nasm, gcc
dotnet publish src/LibertyX.Player -c Release -r win-x64 --self-contained -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64
wine dist/win-x64/LibertyXPlayer.exe --autoteste r.txt video.mp4
```

- `LibertyXPlayer --autoteste relatorio.txt video.mp4`: sem tela; carrega o libmpv, abre o vídeo,
  desenha um quadro na memória e confere que não saiu preto, recusa chave falsa, confere os idiomas.
  O CI roda no executável publicado, no .deb instalado (Ubuntu 24.04), no .rpm (Fedora 41) e no
  .tar.gz (Debian 12, libmpv 0.35).
- `python3 Scripts/textos.py`: todo texto do código está nas tabelas de inglês e espanhol.
- `LIBERTYX_LOG=1`: o diário do Avalonia no terminal e todo o log do mpv em
  `~/.cache/libertyx-player/diario.log` (`LIBERTYX_MPVLOG=v` para o detalhado).
- `LIBERTYX_SW=1`: força o vídeo desenhado pela CPU. `LIBERTYX_LLVMPIPE=1`: aceita o OpenGL do Mesa
  por software (só para teste no Xvfb).

## O vídeo por OpenGL — o que custou descobrir

- O mpv **termina o desenho com o framebuffer 0 ligado**; o do Avalonia é religado logo depois
  (`MpvGlView.OnOpenGlRender`).
- **Abrir arquivo antes de o OpenGL existir desliga o vídeo** ("No render context set"): o player
  fica sempre na árvore (por baixo da biblioteca), e um arquivo vindo da linha de comando espera o
  `Pronto` do controle.
- **Sem OpenGL** (máquina virtual, driver quebrado — e o Avalonia recusa o Mesa por software,
  llvmpipe), o controle nunca abre: em 1,5 s o player troca pelo `MpvSwView`, em que o mpv desenha
  na memória (render API "sw") e a tela copia. As miniaturas usam o mesmo caminho, sem tela.
- O mpv recusa sozinho renderizador por software (preto, sem erro): `gpu-sw=yes`.
- No **Xvfb + llvmpipe** o OpenGL do mpv sai preto em metade das partidas, mesmo com tudo acima —
  peculiaridade de contexto compartilhado no Mesa por software. Na GTX 1660 (driver NVIDIA, KDE)
  sai certo em todas. Teste visual no Xvfb: use o caminho por CPU (é o que o Avalonia escolhe ali).
- O mpv só abre com `LC_NUMERIC=C` (`LibMpv.LocaleC`).

## Estrutura

```
src/LibertyX.Player/
  Mpv/      LibMpv (P/Invoke), MpvCore (opções, comandos, eventos), MpvGlView/MpvSwView,
            Miniaturas (mpv sem tela, render "sw"), SmbStream (lxsmb:// pelo stream_cb)
  Rede/     Smb (servidores salvos, conexão SMBLibrary), Descoberta (avahi + porta 445)
  Core/     Prefs, Textos (+ Tabelas en/es), Pro/Chave, Biblioteca, Escala (só Linux), Win (só Windows), Diario
  Views/    MainWindow, BibliotecaView, PlayerView (+ BarraDeTempo), RedeView, AjustesView/ProDialogo
packaging/linux/   .desktop, metainfo, empacotar.sh (.deb, .rpm, .tar.gz), ícones
packaging/windows/ LibertyXPlayer.iss (Inno Setup), app.ico, artes do instalador
Scripts/   marca.py (símbolo e ícones a partir do vetor do Android), textos.py,
           libmpv-windows.sh (libmpv-2.dll LGPL), instalador.py (ícone e artes do Windows)
```

As telas são montadas em código (`Views/Ui.cs` tem os atalhos); o XAML é só o tema, em `App.axaml`.

## Família LibertyX

[Player Android/TV](https://github.com/fxmx182/libertyx-player-android) ·
[Player iPhone](https://github.com/fxmx182/libertyx-player-ios) ·
[Partition](https://github.com/fxmx182/libertyx-partition) ·
[site](https://fxmx182.github.io)
