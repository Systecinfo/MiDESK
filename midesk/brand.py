#!/usr/bin/env python3
"""Aplica a marca MiDESK e o servidor próprio no código do RustDesk antes do build.

Roda no GitHub Actions logo após o checkout (com submódulos). Variáveis de ambiente:
  MIDESK_APP_NAME  nome do app (padrão: MiDESK)
  MIDESK_SERVER    IP ou domínio do seu servidor hbbs (opcional; sem ele usa o público)
  MIDESK_KEY       chave pública do servidor (conteúdo do id_ed25519.pub; opcional)

Falha de propósito se algum trecho esperado não for encontrado, para que uma mudança
no upstream não gere um build silenciosamente sem a marca.
"""
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = os.environ.get("MIDESK_APP_NAME", "").strip() or "MiDESK"
SERVER = os.environ.get("MIDESK_SERVER", "").strip()
KEY = os.environ.get("MIDESK_KEY", "").strip()


def patch(rel, pairs):
    path = ROOT / rel
    text = path.read_text(encoding="utf-8")
    for pattern, repl in pairs:
        new, n = re.subn(pattern, repl, text, flags=re.M)
        if n == 0:
            sys.exit(f"[brand] padrão não encontrado em {rel}: {pattern}")
        text = new
    path.write_text(text, encoding="utf-8")
    print(f"[brand] {rel}: ok")


def q(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


config = [
    (r'(pub static ref APP_NAME: RwLock<String> = RwLock::new\(")RustDesk("\.to_owned\(\)\);)',
     rf'\g<1>{q(APP)}\g<2>'),
]
if SERVER:
    if not re.fullmatch(r"[A-Za-z0-9.\-:\[\]]+", SERVER):
        sys.exit("[brand] MIDESK_SERVER inválido (use só IP ou domínio, ex.: 203.0.113.10 ou remoto.exemplo.com)")
    config.append((r'(pub const RENDEZVOUS_SERVERS: &\[&str\] = &\[)"[^"]*"(\];)', rf'\g<1>"{SERVER}"\g<2>'))
if KEY:
    if not re.fullmatch(r"[A-Za-z0-9+/=]{40,64}", KEY):
        sys.exit("[brand] MIDESK_KEY inválida (cole o conteúdo do id_ed25519.pub)")
    config.append((r'(pub const RS_PUB_KEY: &str = )"[^"]*"(;)', rf'\g<1>"{KEY}"\g<2>'))
patch("libs/hbb_common/src/config.rs", config)

patch("Cargo.toml", [
    (r'^(description = )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
    (r'^(ProductName = )"RustDesk"', rf'\g<1>"{q(APP)}"'),
    (r'^(FileDescription = )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
])

patch("libs/portable/Cargo.toml", [
    (r'^(description = )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
    (r'^(ProductName = )"RustDesk"', rf'\g<1>"{q(APP)}"'),
    (r'^(FileDescription = )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
])

patch("flutter/windows/runner/Runner.rc", [
    (r'(VALUE "FileDescription", )"RustDesk Remote Desktop"', rf'\g<1>"{q(APP)} Remote Desktop"'),
    (r'(VALUE "ProductName", )"RustDesk"', rf'\g<1>"{q(APP)}"'),
])

print(f"[brand] app={APP} servidor={'próprio: ' + SERVER if SERVER else 'público (padrão)'} "
      f"chave={'própria' if KEY else 'padrão'}")
