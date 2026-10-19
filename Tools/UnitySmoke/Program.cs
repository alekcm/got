using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IronThrone.Core;
using IronThrone.Core.Battle;
using IronThrone.Core.Content;
using IronThrone.Core.Game;
using IronThrone.Unity;
using UnityEngine;

namespace IronThrone.UnitySmoke
{
    /// <summary>
    /// Прогоняет Unity-слой (IMGUI) вне Unity с заглушкой UnityEngine:
    /// меню, все вкладки, тактический бой, сохранение и смена хода.
    /// Ловит NullReference/KeyNotFound, которые компилятор не видит.
    /// </summary>
    internal static class Program
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Application.dataPath = "/home/user/got/Assets";
            Application.persistentDataPath = "/tmp/ironthrone_smoke";
            Application.streamingAssetsPath = "/home/user/got/Assets/StreamingAssets";
            Directory.CreateDirectory(Application.persistentDataPath);

            int failures = 0;
            void Step(string name, Action action)
            {
                try
                {
                    action();
                    Console.WriteLine("  OK   " + name);
                }
                catch (TargetInvocationException ex)
                {
                    failures++;
                    Console.WriteLine("  FAIL " + name + ": " + ex.InnerException);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine("  FAIL " + name + ": " + ex);
                }
            }

            var game = new IronThroneGame();
            Console.WriteLine("Unity-слой: меню и партия");
            Step("Awake/загрузка контента", () => Call(game, "Awake"));
            var content = (ContentDb)Get(game, "_content");
            Console.WriteLine($"  контент: эпох {content?.Eras.Count}, фракций {content?.Factions.Count}");

            Step("OnGUI меню", () => Call(game, "OnGUI"));

            Step("старт партии", () =>
            {
                var faction = content.FactionById["five_stark"];
                var era = content.Eras.First(e => e.id == "fivekings");
                var advisors = content.AdvisorsFor(era, faction).Take(2).Select(a => a.id).ToList();
                Set(game, "_eraId", "fivekings");
                Set(game, "_factionId", "five_stark");
                Set(game, "_mapMode", "canonical");
                Set(game, "_advisors", advisors);
                Call(game, "StartNewGame");
                var state = (GameState)Get(game, "_state");
                if (state == null || state.PlayerFaction != "five_stark")
                    throw new Exception("партия не создана");
            });

            Step("OnGUI игровой экран", () => Call(game, "OnGUI"));
            for (int tab = 0; tab < 9; tab++)
            {
                int t = tab;
                Step("вкладка " + t, () => { Set(game, "_tab", t); Call(game, "OnGUI"); });
            }

            Step("смена хода x3", () =>
            {
                for (int i = 0; i < 3; i++) Call(game, "NextTurn");
                Call(game, "OnGUI");
            });

            Step("освоение земель", () =>
            {
                var state = (GameState)Get(game, "_state");
                var player = state.Faction(state.PlayerFaction);
                var prov = state.Provinces.Values.First(p => p.controller == player.id);
                var improv = TerritorySystem.Improvements(state, player, prov).First();
                if (!TerritorySystem.AddImprovement(state, player, prov, improv.id, false, out var err))
                    throw new Exception("улучшение не добавилось: " + err);
                player.growth = TerritorySystem.GrowthThreshold(state, player);
                player.growthReady = true;
                if (!TerritorySystem.AddImprovement(state, player, prov, improv.id, true, out var err2) &&
                    !TerritorySystem.Improvements(state, player, prov).Any())
                    throw new Exception("освоение не потратилось: " + err2);
                Set(game, "_selectedProvince", prov.id);
                Set(game, "_tab", 0);
                Call(game, "OnGUI");
                Console.WriteLine($"    освоено провинций: {player.claimedProvinces}, улучшений в {prov.def.name}: " +
                                  $"{prov.buildings.Count(id => state.Content.BuildingById.TryGetValue(id, out var b) && b.slot == TerritorySystem.ImprovementSlot)}");
            });

            Step("сохранение/загрузка", () =>
            {
                Call(game, "SaveGame", false);
                Set(game, "_state", null);
                Call(game, "LoadGame");
                if (Get(game, "_state") == null) throw new Exception("состояние не загрузилось");
                Call(game, "OnGUI");
            });

            Step("тактический бой", () =>
            {
                var state = (GameState)Get(game, "_state");
                var player = state.Faction(state.PlayerFaction);
                var army = player.armies.Select(id => state.Armies[id]).FirstOrDefault(a => a.alive && !a.IsEmpty);
                if (army == null) throw new Exception("нет армии игрока");
                string target = state.Province(army.province).def.neighbors
                    .FirstOrDefault(id => state.Province(id)?.controller != null && state.Province(id).controller != player.id);
                if (target == null) throw new Exception("нет соседней чужой провинции");
                var targetProv = state.Province(target);
                var enemyFaction = state.Faction(targetProv.controller);

                if (DiplomacySystem.WarBetween(state, player.id, enemyFaction.id) == null)
                {
                    player.influence = Math.Max(player.influence, 40);
                    var war = DiplomacySystem.DeclareWar(state, player, enemyFaction.id, silent: true);
                    if (war == null) throw new Exception("не удалось объявить войну");
                }

                var enemyArmy = MilitarySystem.CreateArmy(state, enemyFaction, target, "Смоук-гарнизон");
                foreach (var u in content.Units.Where(x => x.faction == "shared").Take(3))
                    enemyArmy.units.Add(new UnitInstance(u.id, u.name) { hp = 100, morale = 100 });
                enemyArmy.province = target;

                bool started = (bool)Call(game, "TryStartBattle", army, target);
                if (!started) throw new Exception("бой не начался");
                Call(game, "OnGUI");
                Call(game, "RebuildBattleTexture");
                var field = (Battlefield)Get(game, "_battle");
                if (field == null || field.units.Count < 2) throw new Exception("поле боя не собрано");

                // несколько действий подряд, включая автоходы защитника
                for (int i = 0; i < 12; i++) Call(game, "TickBattle");
                Call(game, "RebuildBattleTexture");
                Call(game, "OnGUI");
                Call(game, "FinishBattle");
                if (Get(game, "_battle") != null) throw new Exception("бой не закрылся");
                Console.WriteLine($"    боёв в хронике: {state.Battles.Count}, армий игрока: {player.armies.Count}");
                Call(game, "OnGUI");
            });

            Step("смена хода после боя x2", () =>
            {
                for (int i = 0; i < 2; i++) Call(game, "NextTurn");
                Call(game, "OnGUI");
            });

            Console.WriteLine(failures == 0 ? "\nИТОГ: OK" : $"\nИТОГ: ошибок {failures}");
            return failures == 0 ? 0 : 1;
        }

        private static object Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethods(Flags)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
            if (method == null) throw new MissingMethodException(target.GetType().Name + "." + name);
            return method.Invoke(target, args);
        }

        private static object Get(object target, string field)
        {
            var f = target.GetType().GetField(field, Flags);
            if (f == null) throw new MissingFieldException(target.GetType().Name + "." + field);
            return f.GetValue(target);
        }

        private static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, Flags);
            if (f == null) throw new MissingFieldException(target.GetType().Name + "." + field);
            f.SetValue(target, value);
        }
    }
}
