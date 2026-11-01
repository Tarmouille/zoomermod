using System;
using TaskList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<Il2CppSystem.Type, Il2CppSystem.Collections.Generic.List<Player.Lobby.PlayerRole>, bool, Il2CppSystem.Collections.Generic.List<LevelManagement.Levels>>>;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using Player.Tasks;

namespace DaleMod;

// Distribution des tâches (serveur), pour que nos tâches custom tombent comme celles du jeu :
//   bleues = Specialist, rouges = Slacker, noires = Saboteur (couleur = premier rôle de l'entrée, comme Task.GetPlayerRole).
//   - Specialist (et ses sous-rôles Assistant / RH / Analyst) : rouge avec la probabilité du lobby (l'Assistant tire aussi
//     les tâches de Manager marquées ForAssistant) (TaskController.slackerTaskProbability, 30 % par défaut), sinon bleue.
//   - Slacker : noire avec une petite probabilité fixe, sinon rouge ; jamais de bleue.
//   - Dans une couleur, chaque tâche (du jeu ou custom) a la même chance.
// On réécrit les indices que le serveur envoie au joueur (RpcGiveNewTasks) ; les autres rôles ne sont pas touchés.
static class TaskPools
{
    const float SaboteurChance = 0.15f;
    internal static bool Bypass;   // menu debug : assignation forcée, pas de tirage

    static readonly Random rng = new();
    static bool dumped;

    [HarmonyPatch(typeof(TaskController), nameof(TaskController.RpcGiveNewTasks))]
    [HarmonyPrefix]
    static void Redistribute(TaskController __instance, Il2CppSystem.Collections.Generic.List<int> taskIndices)
    {
        if (Bypass || !NetworkServer.active || taskIndices == null || taskIndices.Count == 0) return;
        try
        {
            var lp = __instance.GetComponent<LobbyPlayer>();
            var role = lp?.playerRole;
            var sub = lp?.subRole ?? SubRole.None;
            // Sous-rôle de Specialist (Assistant / RH / Analyst) : traité comme un Specialist, quel que soit le rôle
            // enregistré (on ne sait jamais), sauf pour les autres familles
            if (sub != SubRole.None && role != PlayerRole.Slacker && role != PlayerRole.Manager && role != PlayerRole.Janitor)
                role = PlayerRole.Specialist;
            if (role != PlayerRole.Specialist && role != PlayerRole.Slacker) return;
            var list = __instance.GetEnabledTaskList();
            var pools = Pools(list, OneOnOne.BossPresent(lp));   // un chef autre que ce joueur (ex. l'Assistant seul)
            if (sub == SubRole.Assistant && pools.TryGetValue(PlayerRole.Specialist, out var blue))
            {
                // L'Assistant tire aussi les tâches de Manager prévues pour lui (ex. "1 to 1 Manager"), parmi ses bleues
                blue = new List<int>(blue);
                for (int i = 0; i < list.Count; i++) if (CustomTaskPatches.CustomAt(i, list.Count)?.ForAssistant == true) blue.Add(i);
                pools[PlayerRole.Specialist] = blue;
            }
            if (!dumped) { dumped = true; Dump(list); }

            float red = __instance.slackerTaskProbability;
            if (red > 1f) red /= 100f;   // au cas où le réglage serait en pourcentage

            var taken = new HashSet<int>();
            var before = new List<int>();
            for (int i = 0; i < taskIndices.Count; i++)
            {
                before.Add(taskIndices[i]);
                PlayerRole color = role == PlayerRole.Specialist
                    ? (rng.NextDouble() < red ? PlayerRole.Slacker : PlayerRole.Specialist)
                    : (rng.NextDouble() < SaboteurChance ? PlayerRole.Saboteur : PlayerRole.Slacker);
                if (!pools.TryGetValue(color, out var pool) || pool.Count == 0)
                    color = role == PlayerRole.Specialist ? PlayerRole.Specialist : PlayerRole.Slacker;   // pool vide : couleur de base
                if (!pools.TryGetValue(color, out pool) || pool.Count == 0) { taken.Add(taskIndices[i]); continue; }
                int pick = Pick(pool, taken);
                taken.Add(pick);
                taskIndices[i] = pick;
            }
            Plugin.L.LogInfo($"[POOL] {lp?.username} ({role}{(sub == SubRole.None ? "" : "/" + sub)}, rouge {red:P0}) : " +
                             $"[{string.Join(",", before)}] -> [{string.Join(",", Describe(list, taskIndices))}]");
        }
        catch (Exception e) { Plugin.L.LogError("[POOL] " + e); }
    }

    // Index tiré au hasard dans le pool, en évitant ceux déjà donnés dans ce lot si possible
    static int Pick(List<int> pool, HashSet<int> taken)
    {
        var free = pool.FindAll(x => !taken.Contains(x));
        var from = free.Count > 0 ? free : pool;
        return from[rng.Next(from.Count)];
    }

    // Couleur d'une entrée = son premier rôle
    static Dictionary<PlayerRole, List<int>> Pools(TaskList list, bool managerPresent = true)
    {
        var pools = new Dictionary<PlayerRole, List<int>>();
        for (int i = 0; i < list.Count; i++)
        {
            if (!managerPresent && CustomTaskPatches.CustomAt(i, list.Count)?.RequiresManager == true) continue;   // ex. "1 to 1"
            if (!Karaoke.Available && CustomTaskPatches.CustomAt(i, list.Count)?.Key == Karaoke.Key) continue;   // pack musique absent
            var roles = list[i].Item2;
            if (roles == null || roles.Count == 0) continue;
            var c = roles[0];
            if (!pools.TryGetValue(c, out var p)) pools[c] = p = new List<int>();
            p.Add(i);
        }
        return pools;
    }

    static IEnumerable<string> Describe(TaskList list, Il2CppSystem.Collections.Generic.List<int> indices)
    {
        foreach (var i in indices)
            yield return CustomTaskPatches.CustomTitle(i, list.Count) ?? list[i].Item1.Name.Replace("Task", "");
    }

    // Une fois par partie : le contenu des pools, pour vérifier le classement des tâches du jeu
    static void Dump(TaskList list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var roles = new List<string>();
            if (e.Item2 != null) foreach (var r in e.Item2) roles.Add(r.ToString());
            Plugin.L.LogInfo($"[POOL] #{i} {CustomTaskPatches.CustomTitle(i, list.Count) ?? e.Item1.Name} : {string.Join("+", roles)}");
        }
    }
}
