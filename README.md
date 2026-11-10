# LibertyX Player — Windows e Linux

**Windows:** [instalador (Windows 10/11)](https://github.com/fxmx182/libertyx-player-desktop/releases/download/windows-latest/LibertyXPlayer-Setup.exe)
· [ARM64 (Snapdragon)](https://github.com/fxmx182/libertyx-player-desktop/releases/download/windows-latest/LibertyXPlayer-Setup-arm64.exe)
· [portátil e todos os arquivos](https://github.com/fxmx182/libertyx-player-desktop/releases/tag/windows-latest)

**Linux:** [.deb (Debian, Ubuntu, Mint)](https://github.com/fxmx182/libertyx-player-desktop/releases/latest/download/libertyx-player_amd64.deb)
· [.rpm (Fedora, openSUSE)](https://github.com/fxmx182/libertyx-player-desktop/releases/latest/download/libertyx-player.x86_64.rpm)
· [AppImage (qualquer distribuição)](https://github.com/fxmx182/libertyx-player-desktop/releases/latest/download/LibertyXPlayer-x86_64.AppImage)
· [.tar.gz](https://github.com/fxmx182/libertyx-player-desktop/releases/latest/download/LibertyXPlayer-linux.tar.gz)
· [ARM64 e todos os arquivos](https://github.com/fxmx182/libertyx-player-desktop/releases/latest)

O mesmo player do Android e do iPhone, no desktop. Mesmo nome, mesmas abas (Início, Arquivos,
Procurar, Ajustes), mesma biblioteca com capas e histórico, mesma rolagem quadro a quadro, mesmos
servidores (SMB, WebDAV/Nextcloud, Jellyfin e Emby) — e o mesmo LibertyX Pro, com a mesma chave.

## A escolha do motor: mpv, não VLC

Os irmãos usam o libVLC. Aqui o motor é o **libmpv**, por duas razões que só aparecem no desktop:

| | libVLC no desktop | libmpv |
|---|---|---|
| **Controles por cima do vídeo** | o vídeo é uma janela nativa enfiada na do app; nada desenha por cima dela sem truque de janela transparente (que depende do compositor) | desenha por OpenGL dentro de um controle comum do Avalonia: barra, balão e painéis ficam por cima, como em qualquer tela |
| **Rolagem quadro a quadro** | `setTime` exato, mas lento em arquivo grande | `hr-seek` é o caso de uso principal do mpv; buscas acumuladas durante o arraste são fundidas |
| **SMB** | tem `smb://`, mas a senha na URL é descartada (a armadilha do Android) | o app entrega os bytes pelo `stream_cb` do libmpv: `lxsmb://`, com o SMBLibrary (C# puro), sem senha no endereço |

No Linux o libmpv do .deb/.rpm/.tar.gz é o **da distribuição** (dependência do pacote): o app não
leva FFmpeg nem mpv dentro. Isso deixa o pacote pequeno, recebe as correções de segurança do sistema
e não redistribui código GPL junto com o app.

O **AppImage** é para qualquer distribuição, sem instalar nada: leva um `libmpv.so.2` próprio, LGPL,
compilado por `Scripts/libmpv-linux.sh` (o mesmo caminho do Windows: mpv `-Dgpl=false`, FFmpeg sem
`--enable-gpl`, tudo estático e com os símbolos escondidos). É compilado num Ubuntu 22.04, então abre
em glibc 2.35 ou mais nova (Ubuntu 22.04+, Debian 12+, Fedora 36+, Mint 21+). Do sistema só usa o que
todo desktop tem: ALSA/PulseAudio (o PipeWire atende pelo cliente do Pulse), fontconfig e GnuTLS.
Aceleração: NVDEC (abre o driver da NVIDIA se houver) e VAAPI (libva estático, drivers do sistema).
Tem LuaJIT, então links de sites de vídeo abrem se o yt-dlp estiver instalado. Ao abrir, cria o
atalho no menu (`~/.local/share/applications`) e se atualiza sozinho trocando o próprio arquivo.

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
- **Abas como a dock do celular**: Início (só o que é local: as pastas do computador) e Ajustes
  sempre; até três escolhidas em Ajustes › Barra de abas — Arquivos, Procurar e cada servidor
  (SMB, WebDAV, Jellyfin, Emby, com os símbolos do Jellyfin e do SMB). Sem escolha, Arquivos e
  Procurar. Servidor na barra abre direto nele. O botão da aba sempre abre o começo dela.
- **"Continuar de onde parou?"** ao reabrir qualquer origem (computador, SMB, WebDAV, Jellyfin).
  Visto até o fim (90%, a regra do Jellyfin) apaga a marca.
- **Player**: barra com quadro exato debaixo do mouse, faixas de áudio e legendas (com nome do
  idioma); ao abrir, só a legenda **forçada** liga (a marcada assim ou com "forced"/"forçada" no
  nome), a completa começa desligada; legenda externa (botão ou arrastar o `.srt` para a janela),
  atraso da legenda numa barra no alto (−30 a +30 s, em décimos, com − e +), tamanho da legenda
  (50% a 300%, muda na hora pelo `sub-scale` do mpv), atraso de áudio, velocidade, proporção e zoom, modo noturno, aleatório, repetir, próximo/anterior na pasta,
  tela cheia, informações do vídeo.
- **Abrir com**: o `.desktop` registra os tipos de vídeo; um arquivo aberto assim toca com a pasta
  dele como fila. Endereço http/https/rtsp/m3u8 também (e sites de vídeo, se houver `yt-dlp`).
- **Servidores (Pro)**, o mesmo cadastro do Android e do iPhone:
  - **SMB**: busca automática (anúncio mDNS pelo avahi + varredura da porta 445), compartilhamentos,
    pastas, miniaturas e reprodução direto do servidor.
  - **WebDAV / Nextcloud**: do Nextcloud basta o endereço do servidor (o app acha
    `/remote.php/dav/files/<usuário>/`). O mpv toca a URL com a senha no cabeçalho.
  - **Jellyfin e Emby**, o mesmo cliente do celular e da TV: a tela inicial do servidor
    (continuar, a seguir com a capa da série, TV ao vivo, favoritos, recentes), bibliotecas com a
    ordem e os filtros do servidor (nome, adicionado, lançamento, nota, aleatório; não vistos,
    favoritos, gênero) e a busca (filmes, séries, episódios). Filme ou episódio abre a **página**
    dele: fundo e logotipo, data, duração, a que horas termina, nota, ▶ ↻ ✓ ♥ ⋯ (trailer, série,
    IMDb, TMDB), áudio, legenda e qualidade escolhidos antes de tocar, sinopse, direção e
    roteiro, os outros episódios da temporada, elenco e extras; "Continuar" e "Do início" não
    perguntam de novo no player. Páginas da série (temporadas em abas, próximo episódio) e da
    pessoa (foto, biografia, os títulos dela). Botão direito em qualquer cartão: "Já reproduzido"
    e "Favorito".
  - Toca o **arquivo original** (`static=true`), sem o servidor converter nada — ou, escolhendo
    uma qualidade (20 a 1 Mbps, por conta), a versão HLS que o servidor converte na hora; a
    conversão e o canal ao vivo são fechados ao sair. **Pular abertura/créditos/recapitulação**
    pelos segmentos do Jellyfin 10.10+ (com um plugin que os marque). O app avisa o servidor do
    progresso, então o "continuar" e o "visto" valem nos dois lados.
  - Entrar com usuário e senha (os usuários visíveis do servidor aparecem para escolher) ou com
    **Quick Connect**. A senha não fica guardada, só o token; token vencido com a senha em branco
    entra de novo (usuário sem senha também).
  - Endereço sem `http(s)://` tenta HTTPS primeiro e só cai para HTTP se nem conectar. As
    legendas `.srt/.ass` ao lado do vídeo (e as externas do Jellyfin) vêm junto, como no celular.
  - Os endereços do app (`lxsmb://`, `lxdav://`, `lxmidia://`) são a chave do histórico e das
    marcas e não levam senha nem token: viram URL de verdade só na hora de tocar
    (`Contas.ParaMpv`).
- **Registro de erros** (Ajustes): o que o player abriu, os avisos e erros do mpv, as exceções e
  o aviso de que a sessão anterior caiu; **Copiar** leva o fim dele para colar no suporte. Senhas,
  tokens e chaves saem apagados antes de gravar.
- **Janela de qualquer tamanho**: a barra de cima fica só com os ícones quando os nomes não cabem
  (o nome vai na dica), diálogos e listas encolhem até a largura da janela, filtros quebram a
  linha, e a capa da página do Jellyfin acompanha a largura — inteira no centro, com a mesma
  imagem desfocada dos lados, como no celular deitado. Barras de rolagem sempre à vista.
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
Precisa do `libmpv2` instalado para rodar (ou, sem ele, do libmpv do AppImage:
`Scripts/libmpv-linux.sh` dentro de um `ubuntu:22.04`, ~15 min, e `APPDIR` apontando para ele).

Windows, daqui mesmo do Linux:

```bash
Scripts/libmpv-windows.sh x64 dist/win-x64     # ~10 min na primeira vez; precisa de meson, nasm, gcc
dotnet publish src/LibertyX.Player -c Release -r win-x64 --self-contained -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64
wine dist/win-x64/LibertyXPlayer.exe --autoteste r.txt video.mp4
```

- `LibertyXPlayer --autoteste relatorio.txt video.mp4`: sem tela; carrega o libmpv, abre o vídeo,
  desenha um quadro na memória e confere que não saiu preto, recusa chave falsa, confere os idiomas.
  O CI roda no executável publicado, no .deb instalado (Ubuntu 24.04), no .rpm (Fedora 41), no
  .tar.gz (Debian 12, libmpv 0.35) e no AppImage em Ubuntu 22.04 e Fedora 41 sem mpv nenhum (e
  reprova se ele usar o libmpv do sistema em vez do de dentro).
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
  Rede/     Smb (servidores salvos, conexão SMBLibrary), Descoberta (avahi + porta 445),
            Contas (WebDAV/Jellyfin/Emby + WebDav), ServidorDeMidia (+ AvisoAoServidor), LegendasAoLado,
            Conversao (Qualidade, Conversoes, EscolhaDeFaixas)
  Core/     Prefs, Historico, Abas, Textos (+ Tabelas en/es), Pro/Chave, Biblioteca, Escala (só Linux), Win (só Windows), Diario
  Views/    MainWindow, BibliotecaView (Início), ArquivosView (+ .Servidor: Jellyfin/Emby, + .Paginas:
            filme, série, pessoa, canais), ProcurarView, Cartoes, PlayerView
            (+ BarraDeTempo), AjustesView/ProDialogo
packaging/linux/   .desktop, metainfo, AppRun, empacotar.sh (.deb, .rpm, .tar.gz, AppImage), ícones
packaging/windows/ LibertyXPlayer.iss (Inno Setup), app.ico, artes do instalador
Scripts/   marca.py (símbolo e ícones a partir do vetor do Android), textos.py,
           libmpv-windows.sh (libmpv-2.dll LGPL), libmpv-linux.sh (libmpv.so.2 LGPL do AppImage),
           instalador.py (ícone e artes do Windows)
```

As telas são montadas em código (`Views/Ui.cs` tem os atalhos); o XAML é só o tema, em `App.axaml`.

## Família LibertyX

[Player Android/TV](https://github.com/fxmx182/libertyx-player-android) ·
[Player iPhone](https://github.com/fxmx182/libertyx-player-ios) ·
[Partition](https://github.com/fxmx182/libertyx-partition) ·
[site](https://fxmx182.github.io)
