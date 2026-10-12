using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NunusDecor", "Rust dos Nunus", "1.0.0")]
    [Description("Catálogo visual de decor packs com UI, prévias e ações configuráveis para servidor de testes.")]
    public class NunusDecor : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;

        private const string UsePerm = "nunusdecor.use";
        private const string AdminPerm = "nunusdecor.admin";
        private const string Root = "NunusDecor.UI";
        private const int PageSize = 6;

        private ConfigData configData;
        private readonly Dictionary<ulong, string> selectedPack = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> selectedPage = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> selectedItem = new Dictionary<ulong, string>();

        private class DecorItem
        {
            [JsonProperty("Id")] public string Id;
            [JsonProperty("Pack")] public string Pack;
            [JsonProperty("Name")] public string Name;
            [JsonProperty("Category")] public string Category;
            [JsonProperty("Description")] public string Description;
            [JsonProperty("PreviewImageUrl")] public string PreviewImageUrl;
            [JsonProperty("PreviewImageKey")] public string PreviewImageKey;
            [JsonProperty("Commands")] public List<string> Commands = new List<string>();
            [JsonProperty("AdminOnly")] public bool AdminOnly = true;
        }

        private class ConfigData
        {
            [JsonProperty("EnableExecutionButtons")]
            public bool EnableExecutionButtons = true;

            [JsonProperty("Packs")]
            public List<string> Packs = new List<string> { "Frontier", "Exhibit", "BarGames" };

            [JsonProperty("Items")]
            public List<DecorItem> Items = DefaultItems();
        }

        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData();
            Config.WriteObject(configData, true);
        }

        private void Init()
        {
            permission.RegisterPermission(UsePerm, this);
            permission.RegisterPermission(AdminPerm, this);
            ReadConfigValues();
        }

        private void ReadConfigValues()
        {
            try
            {
                configData = Config.ReadObject<ConfigData>();
            }
            catch
            {
                PrintWarning("Config inválida. Recriando config padrão do NunusDecor.");
                configData = new ConfigData();
                Config.WriteObject(configData, true);
            }

            if (configData == null) configData = new ConfigData();
            if (configData.Packs == null || configData.Packs.Count == 0)
                configData.Packs = new List<string> { "Frontier", "Exhibit", "BarGames" };
            if (configData.Items == null)
                configData.Items = DefaultItems();

            var filtered = new List<DecorItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in configData.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Pack) || string.IsNullOrWhiteSpace(item.Name))
                    continue;
                if (seen.Add(item.Id))
                    filtered.Add(item);
            }
            configData.Items = filtered;
        }

        private void OnServerInitialized()
        {
            RegisterImages();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, Root);
        }

        private bool CanUse(BasePlayer player)
        {
            return player != null && player.IsConnected && permission.UserHasPermission(player.UserIDString, UsePerm);
        }

        private bool IsAdminDecor(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, AdminPerm);
        }

        [ChatCommand("nunusdecor")]
        private void DecorChat(BasePlayer player, string cmd, string[] args)
        {
            OpenUi(player);
        }

        [HookMethod("OpenFor")]
        public void OpenFor(BasePlayer player)
        {
            OpenUi(player);
        }

        private void OpenUi(BasePlayer player)
        {
            if (!CanUse(player))
            {
                SendReply(player, "Sem permissão para usar o NunusDecor.");
                return;
            }

            if (!selectedPack.ContainsKey(player.userID))
                selectedPack[player.userID] = configData.Packs.FirstOrDefault() ?? "Frontier";
            if (!selectedPage.ContainsKey(player.userID))
                selectedPage[player.userID] = 0;
            selectedItem.Remove(player.userID);
            Draw(player);
        }

        [ConsoleCommand("nunusdecor.action")]
        private void DecorAction(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!CanUse(player) || arg.Args == null || arg.Args.Length == 0) return;

            string action = arg.Args[0].ToString();
            if (action == "close")
            {
                selectedItem.Remove(player.userID);
                CuiHelper.DestroyUi(player, Root);
                return;
            }

            if (action == "pack" && arg.Args.Length >= 2)
            {
                string pack = arg.Args[1].ToString();
                if (!configData.Packs.Contains(pack)) return;
                selectedPack[player.userID] = pack;
                selectedPage[player.userID] = 0;
                selectedItem.Remove(player.userID);
                Draw(player);
                return;
            }

            if (action == "page" && arg.Args.Length >= 2)
            {
                int delta;
                if (!int.TryParse(arg.Args[1].ToString(), out delta)) return;
                int current = selectedPage.ContainsKey(player.userID) ? selectedPage[player.userID] : 0;
                selectedPage[player.userID] = Math.Max(0, current + delta);
                Draw(player);
                return;
            }

            if (action == "details" && arg.Args.Length >= 2)
            {
                selectedItem[player.userID] = arg.Args[1].ToString();
                Draw(player);
                return;
            }

            if (action == "back")
            {
                selectedItem.Remove(player.userID);
                Draw(player);
                return;
            }

            if (action == "run" && arg.Args.Length >= 2)
            {
                RunItem(player, arg.Args[1].ToString());
                Draw(player);
                return;
            }
        }

        private void RunItem(BasePlayer player, string id)
        {
            var item = GetItem(id);
            if (item == null)
            {
                SendReply(player, "Item não encontrado.");
                return;
            }

            if (!configData.EnableExecutionButtons)
            {
                SendReply(player, "Os botões de execução estão desabilitados no config.");
                return;
            }

            if (!IsAdminDecor(player))
            {
                SendReply(player, "Este item é somente para administradores/testes.");
                return;
            }

            if (item.Commands == null || item.Commands.Count == 0)
            {
                SendReply(player, "Este item ainda não tem comandos configurados no NunusDecor.json.");
                return;
            }

            var pos = player.transform.position;
            foreach (var raw in item.Commands)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string cmd = raw
                    .Replace("{steamid}", player.UserIDString)
                    .Replace("{userid}", player.UserIDString)
                    .Replace("{name}", player.displayName ?? "player")
                    .Replace("{x}", pos.x.ToString("0.00", CultureInfo.InvariantCulture))
                    .Replace("{y}", pos.y.ToString("0.00", CultureInfo.InvariantCulture))
                    .Replace("{z}", pos.z.ToString("0.00", CultureInfo.InvariantCulture));

                rust.RunServerCommand(cmd);
            }

            SendReply(player, "Ação executada para: " + item.Name);
        }

        private DecorItem GetItem(string id)
        {
            return configData.Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private List<DecorItem> GetCurrentItems(BasePlayer player)
        {
            string pack = selectedPack.ContainsKey(player.userID) ? selectedPack[player.userID] : (configData.Packs.FirstOrDefault() ?? "Frontier");
            return configData.Items.Where(x => string.Equals(x.Pack, pack, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void RegisterImages()
        {
            if (ImageLibrary == null) return;
            foreach (var item in configData.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.PreviewImageUrl)) continue;
                if (!Uri.IsWellFormedUriString(item.PreviewImageUrl, UriKind.Absolute)) continue;
                try
                {
                    ImageLibrary.Call("AddImage", item.PreviewImageUrl, GetImageKey(item), (ulong)0);
                }
                catch (Exception ex)
                {
                    PrintWarning("Falha ao registrar imagem de " + item.Name + ": " + ex.Message);
                }
            }
        }

        private string GetImageKey(DecorItem item)
        {
            return !string.IsNullOrWhiteSpace(item.PreviewImageKey) ? item.PreviewImageKey : "nunusdecor_" + item.Id.ToLowerInvariant();
        }

        private static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string XY(float x, float y)
        {
            return F(x) + " " + F(y);
        }

        private void AddLabel(CuiElementContainer c, string parent, string text, string min, string max, int size = 12, TextAnchor align = TextAnchor.MiddleCenter)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Color = "0.96 0.97 1 1", Align = align },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
        }

        private void AddButton(CuiElementContainer c, string parent, string text, string cmd, string min, string max, string color = "0.19 0.29 0.41 1", int size = 12)
        {
            c.Add(new CuiButton
            {
                Button = { Color = color, Command = cmd },
                Text = { Text = text, FontSize = size, Color = "1 1 1 1", Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
        }

        private void Draw(BasePlayer player)
        {
            if (!CanUse(player)) return;
            CuiHelper.DestroyUi(player, Root);

            var ui = new CuiElementContainer();
            ui.Add(new CuiPanel
            {
                Image = { Color = "0.06 0.09 0.13 0.96" },
                RectTransform = { AnchorMin = "0.08 0.08", AnchorMax = "0.92 0.92" },
                CursorEnabled = true
            }, "Overlay", Root);

            AddLabel(ui, Root, "NUNUS DECOR  |  CATÁLOGO DE DECORAÇÃO", "0.04 0.92", "0.86 0.99", 20);
            AddButton(ui, Root, "X", "nunusdecor.action close", "0.93 0.93", "0.98 0.98", "0.58 0.15 0.17 1", 14);

            for (int i = 0; i < configData.Packs.Count; i++)
            {
                string pack = configData.Packs[i];
                string current = selectedPack.ContainsKey(player.userID) ? selectedPack[player.userID] : configData.Packs[0];
                string label = pack == "BarGames" ? "BAR GAMES" : pack.ToUpperInvariant();
                float x = 0.03f + i * 0.24f;
                AddButton(ui, Root, label, "nunusdecor.action pack " + pack, XY(x, 0.84f), XY(x + 0.21f, 0.90f), current == pack ? "0.19 0.43 0.66 1" : "0.17 0.25 0.33 1", 13);
            }

            string selected;
            if (!selectedItem.TryGetValue(player.userID, out selected) || string.IsNullOrWhiteSpace(selected))
                DrawGrid(player, ui);
            else
                DrawDetails(player, ui, selected);

            CuiHelper.AddUi(player, ui);
        }

        private void DrawGrid(BasePlayer player, CuiElementContainer ui)
        {
            var items = GetCurrentItems(player);
            int totalPages = Math.Max(1, (items.Count + PageSize - 1) / PageSize);
            int currentPage = selectedPage.ContainsKey(player.userID) ? selectedPage[player.userID] : 0;
            currentPage = Math.Min(Math.Max(0, currentPage), totalPages - 1);
            selectedPage[player.userID] = currentPage;

            for (int i = 0; i < PageSize; i++)
            {
                int idx = currentPage * PageSize + i;
                if (idx >= items.Count) break;
                var item = items[idx];
                int col = i % 3;
                int row = i / 3;
                float x = 0.03f + col * 0.31f;
                float top = 0.78f - row * 0.34f;
                string tile = ui.Add(new CuiPanel
                {
                    Image = { Color = "0.12 0.17 0.23 1" },
                    RectTransform = { AnchorMin = XY(x, top - 0.30f), AnchorMax = XY(x + 0.285f, top) }
                }, Root);

                string png = null;
                if (ImageLibrary != null && !string.IsNullOrWhiteSpace(item.PreviewImageUrl))
                {
                    try { png = ImageLibrary.Call("GetImage", GetImageKey(item), (ulong)0) as string; }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(png))
                {
                    ui.Add(new CuiElement
                    {
                        Parent = tile,
                        Components =
                        {
                            new CuiRawImageComponent { Png = png, Color = "1 1 1 1" },
                            new CuiRectTransformComponent { AnchorMin = "0.05 0.33", AnchorMax = "0.95 0.93" }
                        }
                    });
                }
                else
                {
                    AddLabel(ui, tile, string.IsNullOrWhiteSpace(item.PreviewImageUrl) ? "Prévia não configurada" : "Imagem carregando", "0.08 0.48", "0.92 0.78", 12);
                }

                AddLabel(ui, tile, item.Name, "0.03 0.18", "0.97 0.31", 12);
                AddLabel(ui, tile, item.Category ?? "Decor", "0.03 0.10", "0.97 0.18", 10);
                AddButton(ui, tile, "DETALHES", "nunusdecor.action details " + item.Id, "0.08 0.02", "0.92 0.10", "0.13 0.39 0.27 1", 12);
            }

            AddButton(ui, Root, "ANTERIOR", "nunusdecor.action page -1", "0.30 0.03", "0.43 0.08");
            AddLabel(ui, Root, "Página " + (currentPage + 1) + "/" + totalPages, "0.44 0.03", "0.56 0.08", 13);
            AddButton(ui, Root, "PRÓXIMA", "nunusdecor.action page 1", "0.57 0.03", "0.70 0.08");
            AddLabel(ui, Root, "Abra um item para ver descrição, pack e ação configurada.", "0.03 0.005", "0.97 0.03", 10);
        }

        private void DrawDetails(BasePlayer player, CuiElementContainer ui, string id)
        {
            var item = GetItem(id);
            if (item == null)
            {
                selectedItem.Remove(player.userID);
                DrawGrid(player, ui);
                return;
            }

            ui.Add(new CuiPanel
            {
                Image = { Color = "0.10 0.14 0.19 1" },
                RectTransform = { AnchorMin = "0.08 0.15", AnchorMax = "0.92 0.80" }
            }, Root, Root + ".Detail");

            string detail = Root + ".Detail";
            AddButton(ui, detail, "VOLTAR", "nunusdecor.action back", "0.02 0.90", "0.14 0.97", "0.19 0.29 0.41 1");
            AddLabel(ui, detail, item.Name, "0.18 0.88", "0.82 0.97", 20);
            AddLabel(ui, detail, "Pack: " + item.Pack + "  |  Categoria: " + (item.Category ?? "Decor"), "0.18 0.82", "0.82 0.88", 12);

            string png = null;
            if (ImageLibrary != null && !string.IsNullOrWhiteSpace(item.PreviewImageUrl))
            {
                try { png = ImageLibrary.Call("GetImage", GetImageKey(item), (ulong)0) as string; }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(png))
            {
                ui.Add(new CuiElement
                {
                    Parent = detail,
                    Components =
                    {
                        new CuiRawImageComponent { Png = png, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.05 0.14", AnchorMax = "0.48 0.78" }
                    }
                });
            }
            else
            {
                ui.Add(new CuiPanel
                {
                    Image = { Color = "0.12 0.17 0.23 1" },
                    RectTransform = { AnchorMin = "0.05 0.14", AnchorMax = "0.48 0.78" }
                }, detail, detail + ".Preview");
                AddLabel(ui, detail + ".Preview", string.IsNullOrWhiteSpace(item.PreviewImageUrl) ? "Prévia não configurada" : "Imagem carregando", "0.05 0.40", "0.95 0.60", 14);
            }

            string desc = string.IsNullOrWhiteSpace(item.Description) ? "Sem descrição." : item.Description;
            AddLabel(ui, detail, desc, "0.53 0.43", "0.95 0.78", 13, TextAnchor.UpperLeft);

            string info = item.Commands != null && item.Commands.Count > 0
                ? "Comandos configurados: " + item.Commands.Count
                : "Nenhum comando configurado ainda";
            AddLabel(ui, detail, info, "0.53 0.29", "0.95 0.37", 12, TextAnchor.MiddleLeft);

            string adminText = item.AdminOnly ? "Somente admin/testes" : "Liberado para todos com permissão";
            AddLabel(ui, detail, adminText, "0.53 0.23", "0.95 0.29", 11, TextAnchor.MiddleLeft);

            if (configData.EnableExecutionButtons)
            {
                string color = (item.AdminOnly && !IsAdminDecor(player)) ? "0.30 0.30 0.30 1" : "0.13 0.39 0.27 1";
                AddButton(ui, detail, "EXECUTAR / ENTREGAR", "nunusdecor.action run " + item.Id, "0.58 0.10", "0.90 0.18", color, 14);
            }
        }

        private static List<DecorItem> DefaultItems()
        {
            return new List<DecorItem>
            {
                new DecorItem { Id="frontier_hunting_trophy", Pack="Frontier", Name="Hunting Trophy", Category="Trophy", Description="Troféu de caça do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_fish_trophy", Pack="Frontier", Name="Fish Trophy", Category="Trophy", Description="Troféu de peixe do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_bar_doors", Pack="Frontier", Name="Bar Doors", Category="Structure", Description="Portas estilo saloon do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_frontier_planters", Pack="Frontier", Name="Frontier Planters", Category="Gardening", Description="Jardineiras Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_triangle_planter", Pack="Frontier", Name="Triangle Planter", Category="Gardening", Description="Jardineira triangular do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_rocking_chair", Pack="Frontier", Name="Rocking Chair", Category="Furniture", Description="Cadeira de balanço do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_weapon_rack", Pack="Frontier", Name="Weapon Rack", Category="Storage", Description="Expositor de armas do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_storage_barrels", Pack="Frontier", Name="Storage Barrels", Category="Storage", Description="Barris de armazenamento do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_torch_holder", Pack="Frontier", Name="Torch Holder", Category="Lighting", Description="Suporte de tocha do pack Frontier.", AdminOnly=true },
                new DecorItem { Id="frontier_skinning_knife", Pack="Frontier", Name="Skinning Knife", Category="Tool", Description="Faca de esfolar do pack Frontier.", AdminOnly=true },

                new DecorItem { Id="exhibit_clothing_mannequin", Pack="Exhibit", Name="Clothing Mannequin", Category="Decor", Description="Manequim de roupas do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_wall_cabinet", Pack="Exhibit", Name="Wall Cabinet", Category="Storage", Description="Armário de parede do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_fabric_beanbag_seat", Pack="Exhibit", Name="Fabric Beanbag Seat", Category="Furniture", Description="Puff do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_chandelier", Pack="Exhibit", Name="Chandelier", Category="Lighting", Description="Lustre do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_fairy_lights", Pack="Exhibit", Name="Fairy Lights", Category="Lighting", Description="Luzes decorativas do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_bulb_string_lights", Pack="Exhibit", Name="Bulb String Lights", Category="Lighting", Description="Cordão de lâmpadas do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_spot_light", Pack="Exhibit", Name="Spot Light", Category="Lighting", Description="Spot light do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_tripod_spot_light", Pack="Exhibit", Name="Tripod Spot Light", Category="Lighting", Description="Spot light com tripé do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_fluorescent_light", Pack="Exhibit", Name="Fluorescent Light", Category="Lighting", Description="Luz fluorescente do Exhibit Decor Pack.", AdminOnly=true },
                new DecorItem { Id="exhibit_ceiling_fluorescent_light", Pack="Exhibit", Name="Ceiling Fluorescent Light", Category="Lighting", Description="Luz fluorescente de teto do Exhibit Decor Pack.", AdminOnly=true },

                new DecorItem { Id="bargames_pool_table", Pack="BarGames", Name="Pool Table", Category="Game", Description="Mesa de bilhar do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_darts_board", Pack="BarGames", Name="Darts Board", Category="Game", Description="Alvo de dardos do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_jukebox", Pack="BarGames", Name="Jukebox", Category="Entertainment", Description="Jukebox do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_mini_fridge", Pack="BarGames", Name="Mini Fridge", Category="Storage", Description="Mini geladeira do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_bar_stools", Pack="BarGames", Name="Bar Stools", Category="Furniture", Description="Banquetas do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_shotgun_trap", Pack="BarGames", Name="Shotgun Trap Skin", Category="Defense", Description="Skin de shotgun trap do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_wallpaper", Pack="BarGames", Name="Wallpaper", Category="Decor", Description="Papel de parede do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_floor_wallpaper", Pack="BarGames", Name="Floor Wallpaper", Category="Decor", Description="Papel de parede para piso do Bar Games Pack.", AdminOnly=true },
                new DecorItem { Id="bargames_sprays", Pack="BarGames", Name="Sprays", Category="Decor", Description="Sprays do Bar Games Pack.", AdminOnly=true },
            };
        }
    }
}
