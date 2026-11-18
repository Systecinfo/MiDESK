# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

MiDESK is a remote desktop application based on [RustDesk](https://github.com/rustdesk/rustdesk)
(AGPL-3.0). This repository is a visible GitHub fork of RustDesk.

Only **`MiDESK-Setup.exe`** is signed. It is the online installer and updater written and maintained
by this project (source: [`installer/MiDeskSetup.cs`](installer/MiDeskSetup.cs), build script:
[`installer/build.ps1`](installer/build.ps1)). It is built from source by GitHub Actions
([`.github/workflows/midesk-windows.yml`](.github/workflows/midesk-windows.yml)) and published in
[Releases](../../releases). Every signing request is approved manually.

What `MiDESK-Setup.exe` does:

1. Downloads the latest MiDESK package from this repository's GitHub Releases and verifies its SHA-256
   against the published `SHA256SUMS.txt`.
2. Shows what will be installed and asks for confirmation (one UAC prompt).
3. Installs MiDESK into `C:\Program Files\MiDESK` (Windows service, firewall rules, shortcuts).
4. Optionally (checkbox, enabled by default) installs itself as an updater in
   `C:\Program Files\MiDESK Updater` with a scheduled task that checks GitHub Releases at boot and once a day.

Uninstall: *Settings > Apps > MiDESK*. The updater removes its scheduled task and folder on its next run
after MiDESK is uninstalled. Auto-update can also be disabled with
`schtasks /Change /TN "MiDESK Update" /Disable`.

## Team roles

- Committers and reviewers: [Systecinfo](https://github.com/Systecinfo)
- Approvers: [Systecinfo](https://github.com/Systecinfo)

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested
by the user or the person installing or operating it. In detail:

- **MiDESK** registers with the MiDESK connection server chosen by whoever operates this deployment
  (configured at build time). It sends the computer's MiDESK ID, its public key and its IP address, which
  are needed to find the computer. Remote sessions are end-to-end encrypted and are established only when
  someone enters this computer's ID and password (or it is accepted on screen); they go directly between the
  two computers or through the operator's relay server.
- **MiDESK-Setup / updater** contacts `api.github.com` and `github.com` only to check for and download
  releases of this repository ([GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)).
  This can be turned off during installation or later as described above.
- No data is sent to RustDesk or to any other third party.

---

# Política de assinatura de código (português)

Assinatura de código gratuita fornecida pelo [SignPath.io](https://about.signpath.io), certificado da
[SignPath Foundation](https://signpath.org).

- **Arquivo assinado:** somente o `MiDESK-Setup.exe` (instalador e atualizador deste projeto, código em
  `installer/`), compilado pelo GitHub Actions a partir deste repositório. Cada assinatura é aprovada manualmente.
- **Equipe:** desenvolvimento, revisão e aprovação: [Systecinfo](https://github.com/Systecinfo), com
  autenticação de dois fatores no GitHub e no SignPath.
- **Privacidade:** o MiDESK envia o ID, a chave pública e o endereço IP do computador apenas ao servidor de
  conexão do MiDESK. As sessões remotas são criptografadas e só acontecem com o ID e a senha do computador.
  O instalador/atualizador acessa o GitHub somente para verificar e baixar novas versões; a atualização
  automática pode ser desmarcada na instalação ou desligada depois. Nenhum dado é enviado ao RustDesk ou a terceiros.
- **Desinstalar:** Configurações > Aplicativos > MiDESK.
