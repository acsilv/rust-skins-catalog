#!/usr/bin/env python3
"""Atualiza integração NunusHub e adiciona auditoria segura ao NunusDecor.
Execute na VPS após git pull; faz backups e não modifica mundo, dados ou DLC.
"""
from pathlib import Path
import shutil, datetime
P=Path('/home/rust/server/oxide/plugins')
hub=P/'NunusHub.cs'; decor=P/'NunusDecor.cs'
for p in (hub,decor):
    if not p.exists(): raise SystemExit(f'Ausente: {p}')
h=hub.read_text(encoding='utf-8'); d=decor.read_text(encoding='utf-8')
old='object result = target.Call("NunusHub_Open", player);'
new='object result = target.Call(action == "skins" ? "NunusHub_Open" : "OpenFor", player);'
if old in h: h=h.replace(old,new,1)
elif new not in h: raise SystemExit('Chamada de Hub desconhecida; nada foi alterado.')
h=h.replace('"DECORAÇÃO  |  em breve"','"DECORAÇÃO  |  Frontier, Exhibit e Bar Games"')
# O plugin de decoração já expõe OpenFor. Audit precisa estar disponível no console F1.
mark='        private DecorItem GetItem(string id)'
addition='''        // Auditoria por ItemManager: verifica shortnames contra esta instalação real do Rust.
        [ConsoleCommand("nunusdecor.audit")]
        private void AuditDecor(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !IsAdminDecor(player)) return;
            int good = 0, blank = 0, invalid = 0;
            foreach (var entry in configData.Items)
            {
                if (entry == null) continue;
                if (string.IsNullOrWhiteSpace(entry.ItemShortname))
                {
                    blank++;
                    Puts("[DECOR SEM MAPEAMENTO] " + entry.Pack + " | " + entry.Id + " | " + entry.Name);
                    continue;
                }
                var def = ItemManager.FindItemDefinition(entry.ItemShortname);
                if (def == null)
                {
                    invalid++;
                    Puts("[DECOR INVALIDO] " + entry.Pack + " | " + entry.Id + " | " + entry.ItemShortname);
                }
                else good++;
            }
            Puts("[DECOR AUDIT] validos=" + good + " vazios=" + blank + " invalidos=" + invalid);
            if (player != null) SendReply(player, "Auditoria concluída. Confira oxide/logs ou journalctl (DECOR AUDIT).");
        }

        // Lista shortnames verdadeiros do ItemManager (com filtro opcional), sem inventar IDs.
        [ConsoleCommand("nunusdecor.find")]
        private void FindDecor(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !IsAdminDecor(player)) return;
            if (arg.Args == null || arg.Args.Length == 0) return;
            string term = arg.Args[0].ToString().ToLowerInvariant();
            if (term.Length < 3) return;
            int n = 0;
            foreach (var def in ItemManager.itemList)
            {
                if (def == null) continue;
                string display = def.displayName.english.ToString();
                if (def.shortname.ToLowerInvariant().Contains(term) || display.ToLowerInvariant().Contains(term))
                {
                    Puts("[DECOR FIND] " + def.shortname + " | " + display + " | itemid=" + def.itemid);
                    if (++n >= 60) break;
                }
            }
            Puts("[DECOR FIND] resultados mostrados=" + n + " (max 60)");
            if (player != null) SendReply(player, "Busca registrada nos logs do servidor.");
        }

'''
if '[ConsoleCommand("nunusdecor.audit")]' not in d:
    if mark not in d: raise SystemExit('Ponto de inserção do NunusDecor não encontrado; nada alterado.')
    d=d.replace(mark,addition+mark,1)
for p,s in ((hub,h),(decor,d)):
    backup=p.with_suffix('.cs.bak-'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d%H%M%S'))
    shutil.copy2(p,backup)
    p.write_text(s,encoding='utf-8')
    shutil.chown(p,user='rust',group='rust')
    print('Atualizado:',p,'Backup:',backup)
print('Recarregue NunusDecor e NunusHub no console F1; depois rode nunusdecor.audit')
