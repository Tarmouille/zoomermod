using System;
using System.Collections.Generic;
using HarmonyLib;
using Room;
using UnityEngine;

namespace DaleMod;

// Pseudos au-dessus des têtes (NameTag2) : le jeu ne les montre que si la caméra et le joueur sont dans la même pièce,
// d'après ses détecteurs de pièce (RoomTrigger). Sur New York, depuis le retour de l'ascenseur, les pseudos ne
// s'affichent plus : un détecteur reste sans doute bloqué sur une mauvaise pièce. Filet : si le jeu refuse pour une
// différence de pièce alors que nos pièces (Rooms, d'après les volumes) disent "même pièce", on affiche quand même.
// Jamais pour son propre personnage, un joueur viré ou une étiquette masquée exprès (isHidden).
static class NameTagFix
{
    static readonly HashSet<string> logged = new();

    [HarmonyPatch(typeof(NameTag2), nameof(NameTag2.IsVisible))]
    [HarmonyPostfix]
    static void AfterIsVisible(NameTag2 __instance, ref bool __result)
    {
        if (__result) return;
        try
        {
            var tag = __instance;
            if (tag.isHidden) return;
            var pc = tag.playerController;
            if (pc == null || pc.isLocalPlayer) return;
            var lp = pc.TryCast<Player.PlayerController>()?.lobbyPlayer;
            if (lp != null && lp.isFired) return;
            RoomController gameCam = tag.mainCamera?.currentRoom, gamePlayer = tag.roomTrigger?.currentRoom;
            if (gameCam == gamePlayer) return;   // le jeu cache pour une autre raison (mur, distance…) : on laisse
            var cam = Camera.main;
            if (cam == null) return;
            var p = cam.transform.position;
            var ourCam = Rooms.At(p) ?? Rooms.At(p + Vector3.down);
            var ourPlayer = Rooms.Of(pc);
            bool same = ourCam != null && ourPlayer != null
                && (ourCam == ourPlayer || (!string.IsNullOrEmpty(ourCam.roomName) && ourCam.roomName == ourPlayer.roomName));
            string key = $"{Name(gameCam)}|{Name(gamePlayer)}|{Name(ourCam)}|{Name(ourPlayer)}";
            if (logged.Count < 50 && logged.Add(key))
                Plugin.L.LogInfo($"[NAMETAG] pseudo caché par le jeu : caméra '{Name(gameCam)}', joueur '{Name(gamePlayer)}' ; " +
                                 $"d'après nos pièces : caméra '{Name(ourCam)}', joueur '{Name(ourPlayer)}' -> {(same ? "affiché" : "laissé caché")}");
            if (same) __result = true;
        }
        catch (Exception e) { Plugin.L.LogWarning("[NAMETAG] " + e.Message); }
    }

    static string Name(RoomController r) => r == null ? "-" : string.IsNullOrEmpty(r.subRoomName) ? r.roomName : r.roomName + "/" + r.subRoomName;
}
