# LibertyX Player no Flathub

O Flathub é a loja que aparece na COSMIC Store (Pop!_OS), no Linux Mint, no Fedora, no Zorin e no
GNOME Software/Discover de quem ativou o Flathub. O ID do app é
`io.github.fxmx182.libertyx-player-desktop`. O Flathub exige que o ID aponte para um repositório que
existe (`github.com/fxmx182/libertyx-player-desktop`). Na loja aparece só
"LibertyX Player".

## O que tem aqui

| Arquivo | Para quê |
|---|---|
| `io.github.fxmx182.libertyx-player-desktop.yml` | o manifesto: libass, libplacebo e libmpv compilados do código, e o app vindo do `.tar.gz` da release do Linux |
| `….metainfo.xml` | a ficha da loja (descrição em en/pt-BR/es, capturas, cores); o `<release>` com a versão e a data entra na compilação, lido do `versao.txt` da release |
| `….desktop` | o atalho (com os tipos de vídeo, para "Abrir com" e para ser o player padrão) |
| `libertyx-player.sh` | abre o app com a pasta de extração do .NET dentro do cache do app (a pasta pessoal não tem escrita na caixinha) |
| `screenshots/` | as capturas da ficha (o Flathub copia do GitHub na compilação) |

Dentro do Flatpak o app não oferece atualização (quem atualiza é a loja). A pergunta "usar como
player padrão?" só explica onde escolher, porque o Flatpak não deixa o app mudar isso sozinho.

## Preencher com a release mais nova

```
python3 Scripts/flathub.py
```

Troca a tag, o commit, os endereços e os sha256 pelos da release mais nova do Linux. Precisa ser
uma release com `data=` no `versao.txt` (a partir da 1.0.15).

## Testar a compilação aqui

Na imagem `ghcr.io/flathub-infra/flatpak-github-actions:freedesktop-26.08` (já tem o
flatpak-builder, o SDK 26.08 e o `flatpak-builder-lint`):

```
docker run --rm --privileged -v "$PWD":/w -w /w --entrypoint sh \
  ghcr.io/flathub-infra/flatpak-github-actions:freedesktop-26.08 -c '
  flatpak-builder --disable-rofiles-fuse --force-clean --repo=/tmp/repo /tmp/build \
    packaging/flatpak/io.github.fxmx182.libertyx-player-desktop.yml &&
  flatpak-builder-lint manifest packaging/flatpak/io.github.fxmx182.libertyx-player-desktop.yml &&
  flatpak-builder-lint repo /tmp/repo'
```

## Enviar para o Flathub (feito pelo dono, pela conta fxmx182)

1. Fazer um fork de https://github.com/flathub/flathub e criar um branch a partir do branch `new-pr`.
2. Colocar nele só o manifesto preenchido (`io.github.fxmx182.libertyx-player-desktop.yml`).
3. Abrir o pull request contra o `new-pr`, com o título `Add io.github.fxmx182.libertyx-player-desktop`.
   Comentar `bot, build` para o Flathub compilar um teste.
4. Os revisores do Flathub respondem no próprio pull request (costuma levar de alguns dias a
   algumas semanas). Pontos que eles podem perguntar: o app é proprietário com o código público, e
   o executável vem pronto da release, compilado pelo CI deste repositório a partir do código.
5. Aprovado, o Flathub cria `github.com/flathub/io.github.fxmx182.libertyx-player-desktop`. Cada
   versão nova vira um pull request automático lá (o `x-checker-data` do manifesto), ou rodando
   `Scripts/flathub.py` e enviando o manifesto preenchido.
6. Em https://flathub.org/, entrar com o GitHub e verificar o app (o selo de "verificado" vem da
   conta fxmx182 ser a dona do `io.github.fxmx182`).
