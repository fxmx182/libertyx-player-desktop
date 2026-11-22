# LibertyX Player em Flatpak (repositório próprio)

O Flatpak do LibertyX fica num repositório nosso, no GitHub Pages deste repositório:

- página com o botão de instalar: https://fxmx182.github.io/libertyx-player-desktop/
- o `.flatpakref` (abre na COSMIC Store, no GNOME Software e no Discover, instala e atualiza):
  https://fxmx182.github.io/libertyx-player-desktop/libertyx.flatpakref
- o repositório OSTree: https://fxmx182.github.io/libertyx-player-desktop/repo/

O ID do app é `io.github.fxmx182.libertyx-player-desktop` e o branch é `stable`. O runtime
(`org.freedesktop.Platform` 26.08) vem do Flathub: o `.flatpakref` adiciona o Flathub se faltar.

**Não é o Flathub.** A política do Flathub proíbe manifesto e pull request de envio feitos por IA
(https://docs.flathub.org/docs/for-app-authors/requirements#generative-ai-policy), e estes foram.

## Como sai uma versão

1. Um commit `Linux:` publica a release do Linux (workflow `linux.yml`).
2. Ao terminar, o `flatpak.yml` vê que a release mais nova não é a que está no Pages (`tag.txt`) e:
   - preenche o manifesto com a release (`Scripts/flathub.py <tag>`);
   - compila no x86_64 e no aarch64, cada um no próprio runner, com o cache do flatpak-builder;
   - junta os dois num repositório só (sem o `.Debug`), assina com a chave do segredo
     `FLATPAK_GPG_KEY` e gera o catálogo (appstream) e os deltas;
   - publica no Pages com a página, o `.flatpakref`, o `.flatpakrepo` e a chave pública.
3. Quem tem o app recebe a atualização pela loja (ou `flatpak update`).

Pelo botão "Run workflow" do `flatpak.yml` dá para republicar sem release nova.

## O que tem aqui

| Arquivo | Para quê |
|---|---|
| `io.github.fxmx182.libertyx-player-desktop.yml` | o manifesto: libass, libplacebo e libmpv (LGPL, `-Dgpl=false`) compilados do código sobre o FFmpeg do runtime, e o app vindo do `.tar.gz` da release do Linux |
| `….metainfo.xml` | a ficha que a loja mostra (en/pt-BR/es, capturas, cores); o `<release>` com a versão e a data entra na compilação, lido do `versao.txt` da release |
| `….desktop` | o atalho (com os tipos de vídeo, para "Abrir com" e para ser o player padrão) |
| `libertyx-player.sh` | abre o app com a extração do .NET no cache do app (a pasta pessoal não tem escrita na caixinha) |
| `libertyx.gpg` | a chave pública que assina o repositório (vai dentro do `.flatpakref`) |
| `pagina/` | a página de instalar, o `.flatpakref` e o `.flatpakrepo` (o workflow troca `@SITE@`, `@GPGKEY@` e `@TAG@`) |
| `screenshots/` | as capturas da ficha |

A chave privada fica no segredo `FLATPAK_GPG_KEY` do repositório e, de reserva, em
`/DATA/AppData/libertyx-flatpak/` no servidor (`privada.asc`, `gnupg/`). Perder a chave obriga
todo mundo a reinstalar; trocar a chave também.

Dentro do Flatpak o app não oferece atualização (quem atualiza é a loja). A pergunta "usar como
player padrão?" só explica onde escolher, porque o Flatpak não deixa o app mudar isso sozinho.

## Testar aqui

Na imagem `ghcr.io/flathub-infra/flatpak-github-actions:freedesktop-26.08` (flatpak-builder, SDK
26.08 e `flatpak-builder-lint`), com `--privileged` e `git config --global --add safe.directory '*'`:

```
python3 Scripts/flathub.py <tag> /tmp/manifesto.yml
flatpak-builder --disable-rofiles-fuse --force-clean --default-branch=stable --repo=repo build /tmp/manifesto.yml
flatpak build-bundle repo teste.flatpak io.github.fxmx182.libertyx-player-desktop stable
```
