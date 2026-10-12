#!/usr/bin/env python3
from pathlib import Path
import shutil

root = Path('/home/rust/server/oxide/plugins')
patches = [
    ('NunusSkinsUI.cs', '[ChatCommand("skinsui")]', '''        [HookMethod("NunusHub_Open")]
        private object OpenFromNunusHub(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return "Jogador indisponível.";
            ChatOpen(player, "skinsui", new string[0]);
            return null;
        }

'''),
    ('NunusBuildingThemes.cs', '[ChatCommand("nunusbuild")]', '''        [HookMethod("NunusHub_Open")]
        private object OpenFromNunusHub(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return "Jogador indisponível.";
            Open(player, "nunusbuild", new string[0]);
            return null;
        }

'''),
]
for filename, marker, hook in patches:
    path = root / filename
    if not path.exists():
        print(f'PENDENTE: {filename} não encontrado')
        continue
    s = path.read_text(encoding='utf-8')
    if '[HookMethod("NunusHub_Open")]' in s:
        print(f'OK: {filename} já tem integração')
        continue
    pos = s.find(marker)
    if pos == -1:
        print(f'ERRO: comando não encontrado em {filename}, sem modificações')
        continue
    shutil.copy2(path, path.with_suffix('.cs.pre-nunushub.bak'))
    s = s[:pos] + hook + '        ' + s[pos:]
    path.write_text(s, encoding='utf-8')
    print(f'INTEGRADO: {filename}')
