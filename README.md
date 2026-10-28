# LibertyX Player — Linux

**Baixar:** [.deb (Debian, Ubuntu, Mint)](https://github.com/fxmx182/libertyx-player-linux/releases/latest/download/libertyx-player_amd64.deb)
· [.rpm (Fedora, openSUSE)](https://github.com/fxmx182/libertyx-player-linux/releases/latest/download/libertyx-player.x86_64.rpm)
· [.tar.gz](https://github.com/fxmx182/libertyx-player-linux/releases/latest/download/LibertyXPlayer-linux.tar.gz)
· [ARM64 e todos os arquivos](https://github.com/fxmx182/libertyx-player-linux/releases/latest)

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

O libmpv é o **da distribuição** (dependência do .deb/.rpm): o app não leva FFmpeg nem mpv dentro.
Isso deixa o pacote pequeno, recebe as correções de segurança do sistema e não redistribui código
GPL junto com o app.

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
  Core/     Prefs, Textos (+ Tabelas en/es), Pro/Chave, Biblioteca, Escala, Diario
  Views/    MainWindow, BibliotecaView, PlayerView (+ BarraDeTempo), RedeView, AjustesView/ProDialogo
packaging/linux/   .desktop, metainfo, empacotar.sh (.deb, .rpm, .tar.gz), ícones
Scripts/   marca.py (símbolo e ícones a partir do vetor do Android), textos.py
```

As telas são montadas em código (`Views/Ui.cs` tem os atalhos); o XAML é só o tema, em `App.axaml`.

## Família LibertyX

[Player Android/TV](https://github.com/fxmx182/libertyx-player-android) ·
[Player iPhone](https://github.com/fxmx182/libertyx-player-ios) ·
[Partition](https://github.com/fxmx182/libertyx-partition) ·
[site](https://fxmx182.github.io)
