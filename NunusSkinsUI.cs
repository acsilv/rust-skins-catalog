using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NunusSkinsUI", "Rust dos Nunus", "1.0.0")]
    [Description("Visual, permission-gated inventory skin browser with paging and favorites; reads the existing Skins catalog.")]
    public class NunusSkinsUI : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;

        private const string PermUse = "nunusskinsui.use";
        private const string PermAdmin = "nunusskinsui.admin";
        private const string Ui = "NunusSkinsUI.Root";
        private const int PageSize = 12;
        private const int MaximumCatalogPerItem = 2000;
        private const string ConfigPath = "Skins";
        private Dictionary<string, List<ulong>> catalog = new Dictionary<string, List<ulong>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();
        private StoredData stored;

        private class Session
        {
            public ulong ItemUid;
            public string Shortname;
            public int Page;
            public bool FavoritesOnly;
            public bool ChoosingItem = true;
        }

        private class StoredData
        {
            public Dictionary<ulong, Dictionary<string, List<ulong>>> Favorites = new Dictionary<ulong, Dictionary<string, List<ulong>>>();
        }

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            LoadStoredData();
            LoadCatalog();
        }

        private void OnServerInitialized() { LoadCatalog(); }
        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList) CuiHelper.DestroyUi(player, Ui);
            SaveStoredData();
        }
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            sessions.Remove(player.userID);
        }

        private void LoadStoredData()
        {
            try { stored = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name); }
            catch { stored = new StoredData(); }
            if (stored == null) stored = new StoredData();
            if (stored.Favorites == null) stored.Favorites = new Dictionary<ulong, Dictionary<string, List<ulong>>>();
        }
        private void SaveStoredData() { Interface.Oxide.DataFileSystem.WriteObject(Name, stored); }

        private void LoadCatalog()
        {
            var next = new Dictionary<string, List<ulong>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var config = Interface.Oxide.ConfigFileSystem.ReadObject<JObject>(ConfigPath);
                var entries = config["Skins"] as JArray;
                if (entries == null) { PrintWarning("oxide/config/Skins.json has no Skins array."); return; }
                foreach (var row in entries.OfType<JObject>())
                {
                    string shortname = (string)row["Item Shortname"];
                    if (String.IsNullOrWhiteSpace(shortname) || ItemManager.FindItemDefinition(shortname) == null) continue;
                    var ids = row["Skins"] as JArray;
                    if (ids == null) continue;
                    List<ulong> result = new List<ulong>();
                    foreach (var token in ids)
                    {
                        ulong id;
                        if (ulong.TryParse(token.ToString(), out id) && id > 0 && !result.Contains(id)) result.Add(id);
                        if (result.Count >= MaximumCatalogPerItem) break;
                    }
                    if (result.Count > 0) next[shortname] = result;
                }
                catalog = next;
                Puts("Loaded {0} item types and {1} skin IDs from Skins.json", catalog.Count, catalog.Values.Sum(x => x.Count));
            }
            catch (Exception ex) { PrintError("Could not read existing Skins.json: " + ex.Message); }
        }

        [ChatCommand("skinsui")]
        private void ChatOpen(BasePlayer player, string command, string[] args)
        {
            if (!Allowed(player)) { SendReply(player, "Você não tem permissão para abrir o painel."); return; }
            sessions[player.userID] = new Session();
            Draw(player);
        }

        [ConsoleCommand("nunusskinsui.reloadcatalog")]
        private void ReloadCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;
            if (player == null && arg.Connection != null) return;
            LoadCatalog();
            if (player != null) { sessions[player.userID] = new Session(); Draw(player); }
            Puts("NunusSkinsUI catalog reloaded.");
        }

        private bool Allowed(BasePlayer player) => player != null && player.IsConnected && permission.UserHasPermission(player.UserIDString, PermUse);

        private List<Item> Inventory(BasePlayer player)
        {
            var result = new List<Item>();
            if (player.inventory == null) return result;
            if (player.inventory.containerMain != null) result.AddRange(player.inventory.containerMain.itemList);
            if (player.inventory.containerBelt != null) result.AddRange(player.inventory.containerBelt.itemList);
            if (player.inventory.containerWear != null) result.AddRange(player.inventory.containerWear.itemList);
            return result.Where(x => x != null && x.info != null && catalog.ContainsKey(x.info.shortname)).ToList();
        }

        private Item GetSelected(BasePlayer player, Session session)
        {
            return Inventory(player).FirstOrDefault(x => x.uid.Value == session.ItemUid && x.info.shortname == session.Shortname);
        }

        [ConsoleCommand("nunusskinsui.action")]
        private void Action(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!Allowed(player) || arg.Args == null || arg.Args.Length == 0) return;
            Session state;
            if (!sessions.TryGetValue(player.userID, out state)) return;
            string action = arg.Args[0];
            if (action == "close") { CuiHelper.DestroyUi(player, Ui); sessions.Remove(player.userID); return; }
            if (action == "inventory") { state.ChoosingItem = true; state.Page = 0; Draw(player); return; }
            if (action == "select")
            {
                if (arg.Args.Length != 2) return;
                ulong uid;
                if (!ulong.TryParse(arg.Args[1], out uid)) return;
                var item = Inventory(player).FirstOrDefault(x => x.uid.Value == uid);
                if (item == null) return;
                state.ItemUid = uid;
                state.Shortname = item.info.shortname;
                state.Page = 0;
                state.FavoritesOnly = false;
                state.ChoosingItem = false;
                Draw(player);
                return;
            }
            var selected = GetSelected(player, state);
            if (selected == null) { state.ChoosingItem = true; Draw(player); return; }
            if (action == "page")
            {
                if (arg.Args.Length != 2) return;
                int delta;
                if (!int.TryParse(arg.Args[1], out delta) || Math.Abs(delta) != 1) return;
                state.Page = Math.Max(0, state.Page + delta);
            }
            else if (action == "favorites") { state.FavoritesOnly = !state.FavoritesOnly; state.Page = 0; }
            else if (action == "favorite" || action == "apply")
            {
                if (arg.Args.Length != 2) return;
                ulong skin;
                if (!ulong.TryParse(arg.Args[1], out skin) || !catalog[state.Shortname].Contains(skin)) return;
                if (action == "favorite") ToggleFavorite(player.userID, state.Shortname, skin);
                else
                {
                    // Server-side validate possession and item ID again before changing the skin.
                    selected = GetSelected(player, state);
                    if (selected == null || selected.IsLocked()) return;
                    selected.skin = skin;
                    selected.MarkDirty();
                    BaseEntity held = selected.GetHeldEntity();
                    if (held != null) { held.skinID = skin; held.SendNetworkUpdate(); }
                    SendReply(player, "Skin aplicada. Se a imagem não carregar, confira se o cliente possui acesso a essa skin.");
                }
            }
            Draw(player);
        }

        private List<ulong> Favorites(ulong userId, string shortname)
        {
            Dictionary<string, List<ulong>> items;
            if (!stored.Favorites.TryGetValue(userId, out items)) { items = new Dictionary<string, List<ulong>>(); stored.Favorites[userId] = items; }
            List<ulong> skins;
            if (!items.TryGetValue(shortname, out skins)) { skins = new List<ulong>(); items[shortname] = skins; }
            return skins;
        }
        private void ToggleFavorite(ulong userId, string shortname, ulong skin)
        {
            var fav = Favorites(userId, shortname);
            if (fav.Contains(skin)) fav.Remove(skin); else fav.Add(skin);
            SaveStoredData();
        }

        private void Label(CuiElementContainer c, string parent, string text, string min, string max, int size = 14, TextAnchor align = TextAnchor.MiddleCenter, string color = "0.92 0.96 1 1")
        {
            c.Add(new CuiLabel { Text = { Text = text, FontSize = size, Align = align, Color = color }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent);
        }
        private void Button(CuiElementContainer c, string parent, string text, string cmd, string min, string max, string color = "0.19 0.30 0.43 1", int font = 13)
        {
            c.Add(new CuiButton { Button = { Command = cmd, Color = color }, Text = { Text = text, FontSize = font, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent);
        }
        private void Draw(BasePlayer player)
        {
            if (!Allowed(player)) return;
            Session state;
            if (!sessions.TryGetValue(player.userID, out state)) return;
            CuiHelper.DestroyUi(player, Ui);
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.06 0.09 0.13 0.97" }, RectTransform = { AnchorMin = "0.11 0.11", AnchorMax = "0.89 0.89" }, CursorEnabled = true }, "Overlay", Ui);
            Label(c, Ui, "RUST DOS NUNUS  |  SKIN SELECTOR", "0.03 0.92", "0.84 0.99", 20, TextAnchor.MiddleLeft);
            Button(c, Ui, "X", "nunusskinsui.action close", "0.93 0.925", "0.975 0.98", "0.55 0.18 0.18 1");
            Button(c, Ui, "INVENTÁRIO", "nunusskinsui.action inventory", "0.03 0.85", "0.20 0.91");
            if (state.ChoosingItem)
            {
                Label(c, Ui, "Selecione um item do seu inventário para visualizar skins", "0.22 0.85", "0.94 0.91", 14, TextAnchor.MiddleLeft);
                var items = Inventory(player).Take(30).ToList();
                if (items.Count == 0) Label(c, Ui, "Nenhum item compatível no inventário principal, cinto ou roupas.", "0.05 0.38", "0.95 0.65");
                for (int i = 0; i < items.Count; i++)
                {
                    int col = i % 5, row = i / 5;
                    float x = 0.035f + col * 0.188f;
                    float y = 0.75f - row * 0.112f;
                    string itemName = items[i].info.displayName.english;
                    if (itemName.Length > 19) itemName = itemName.Substring(0, 16) + "...";
                    Button(c, Ui, itemName + "\nx" + items[i].amount, "nunusskinsui.action select " + items[i].uid.Value, Rect(x, y - 0.09f), Rect(x + 0.177f, y), "0.14 0.21 0.29 1", 11);
                }
            }
            else
            {
                var item = GetSelected(player, state);
                if (item == null) { state.ChoosingItem = true; Draw(player); return; }
                string shortname = item.info.shortname;
                var favorites = Favorites(player.userID, shortname);
                var valid = catalog[shortname];
                var options = state.FavoritesOnly ? valid.Where(favorites.Contains).ToList() : valid;
                int pages = Math.Max(1, (options.Count + PageSize - 1) / PageSize);
                state.Page = Math.Min(Math.Max(0, state.Page), pages - 1);
                Label(c, Ui, item.info.displayName.english + "  |  " + options.Count + " skins", "0.22 0.85", "0.66 0.91", 15, TextAnchor.MiddleLeft);
                Button(c, Ui, state.FavoritesOnly ? "TODAS" : "FAVORITAS", "nunusskinsui.action favorites", "0.71 0.85", "0.91 0.91");
                for (int i = 0; i < PageSize; i++)
                {
                    int index = state.Page * PageSize + i;
                    if (index >= options.Count) break;
                    ulong skin = options[index];
                    int col = i % 4, row = i / 4;
                    float x1 = 0.035f + col * 0.238f, yTop = 0.81f - row * 0.225f;
                    string tile = c.Add(new CuiPanel { Image = { Color = "0.12 0.17 0.23 1" }, RectTransform = { AnchorMin = Rect(x1, yTop - 0.21f), AnchorMax = Rect(x1 + 0.225f, yTop) } }, Ui);
                    // ImageLibrary supplies a PNG on demand. Missing images show a readable ID placeholder.
                    string png = null;
                    if (ImageLibrary != null)
                    {
                        try { png = ImageLibrary.Call("GetImage", shortname, skin) as string; } catch { }
                    }
                    if (!String.IsNullOrEmpty(png))
                    {
                        c.Add(new CuiElement { Parent = tile, Components = { new CuiRawImageComponent { Png = png, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.31 0.27", AnchorMax = "0.69 0.95" } } });
                    }
                    else Label(c, tile, "Sem miniatura", "0.1 0.38", "0.9 0.87", 11, TextAnchor.MiddleCenter, "0.6 0.7 0.8 1");
                    Label(c, tile, "Workshop " + skin, "0.04 0.22", "0.96 0.38", 10);
                    Button(c, tile, "APLICAR", "nunusskinsui.action apply " + skin, "0.035 0.035", "0.68 0.21", "0.12 0.40 0.27 1", 10);
                    Button(c, tile, favorites.Contains(skin) ? "★" : "☆", "nunusskinsui.action favorite " + skin, "0.72 0.035", "0.965 0.21", "0.37 0.29 0.15 1", 16);
                }
                Button(c, Ui, "◀ ANTERIOR", "nunusskinsui.action page -1", "0.22 0.035", "0.42 0.09");
                Label(c, Ui, "Página " + (state.Page + 1) + "/" + pages, "0.43 0.035", "0.57 0.09", 13);
                Button(c, Ui, "PRÓXIMA ▶", "nunusskinsui.action page 1", "0.58 0.035", "0.78 0.09");
            }
            CuiHelper.AddUi(player, c);
        }
        private string Rect(float x, float y) => x.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " " + y.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
