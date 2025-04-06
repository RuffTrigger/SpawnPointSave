using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using System;
using TShockAPI.DB;
using System.Collections.Generic;

[ApiVersion(2, 1)]
public class SpawnPointSave : TerrariaPlugin
{
    private Dictionary<int, bool> playerUsedRecall = new Dictionary<int, bool>();

    public override string Name => "SpawnPointSave";
    public override Version Version => new Version(1, 9);
    public override string Author => "Ruff Trigger";
    public override string Description => "Automatically saves player spawn points, teleports them upon respawn, and redirects recall to their set spawn.";

    public SpawnPointSave(Main game) : base(game) { }

    public override void Initialize()
    {
        EnsureDatabaseSchema();
        TShockAPI.Hooks.PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        Commands.ChatCommands.Add(new Command("spawnpoint.set", SetSpawnPoint, "setspawnpoint"));
    }

    private void EnsureDatabaseSchema()
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS PlayerSpawns (
                UserID INTEGER PRIMARY KEY,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL
            );";

        try
        {
            TShock.DB.Query(sql);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"Error ensuring database schema: {ex.Message}");
        }
    }

    private void SaveAndSetSpawnPoint(TSPlayer player)
    {
        try
        {
            // Save the spawn point to the database
            TShock.DB.Query("INSERT OR REPLACE INTO PlayerSpawns (UserID, X, Y) VALUES (@0, @1, @2);",
                            player.Account.ID, player.TileX, player.TileY);

            // Set the player's spawn point in-game
            Main.player[player.Index].SpawnX = player.TileX;
            Main.player[player.Index].SpawnY = player.TileY;

            player.SendSuccessMessage($"Your spawn point has been set to ({player.TileX}, {player.TileY}).");
            //TShock.Log.ConsoleInfo($"Spawn point for {player.Name} saved and set at ({player.TileX}, {player.TileY}).");
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"Error saving and setting spawn point for {player.Name}: {ex.Message}");
        }
    }

    private void OnPlayerPostLogin(TShockAPI.Hooks.PlayerPostLoginEventArgs args)
    {
        var player = args.Player;
        if (player != null && player.IsLoggedIn)
        {
            try
            {
                using (var reader = TShock.DB.QueryReader("SELECT X, Y FROM PlayerSpawns WHERE UserID = @0;", player.Account.ID))
                {
                    if (reader.Read())
                    {
                        int x = reader.Get<int>("X");
                        int y = reader.Get<int>("Y");
                        player.SendSuccessMessage($"Teleporting you home, {player.Name}!");
                        player.Teleport(x * 16, y * 16); // Teleport to saved spawn point (convert to pixels)

                        // Set the player's spawn properties
                        Main.player[player.Index].SpawnX = x;
                        Main.player[player.Index].SpawnY = y;

                        //TShock.Log.ConsoleInfo($"Teleported {player.Name} to their saved spawn point at ({x}, {y}).");
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"Error loading spawn point for {player.Name}: {ex.Message}");
            }
        }
    }

    private async void OnGameUpdate(EventArgs args)
    {
        foreach (TSPlayer player in TShock.Players)
        {
            if (player == null || !player.Active || !player.IsLoggedIn)
                continue;

            // Check if the player is using an item (itemAnimation > 0)
            if (Main.player[player.Index].itemAnimation > 0)
            {
                int itemType = Main.player[player.Index].inventory[Main.player[player.Index].selectedItem].type;

                if (itemType == 50 || itemType == 2350 || itemType == 3199 || itemType == 3124 || itemType == 4263) // Magic Mirror, Recall Potion, etc.
                {
                    if (!playerUsedRecall[player.Index]) // Only act once per use
                    {
                        playerUsedRecall[player.Index] = true;


                        using (var reader = TShock.DB.QueryReader("SELECT X, Y FROM PlayerSpawns WHERE UserID = @0;", player.Account.ID))
                        {
                            if (reader.Read())
                            {
                                player.SendSuccessMessage($"Teleporting you home " + player.Name + "!");
                                int x = reader.Get<int>("X");
                                int y = reader.Get<int>("Y");

                                // Delay the teleportation by 1.2 seconds to override the default behavior
                                await Task.Delay(1070);
                                player.Teleport(x * 16, y * 16); // Teleport to saved spawn point (convert to pixels)

                                // Set the player's spawn properties
                                Main.player[player.Index].SpawnX = x;
                                Main.player[player.Index].SpawnY = y;
                                //TShock.Log.ConsoleInfo($"Redirected recall/mirror for {player.Name} to their saved spawn point at ({x}, {y}).");

                                // Cancel further item use by resetting item animation
                                Main.player[player.Index].itemAnimation = 0;
                                Main.player[player.Index].itemTime = 0;
                            }
                        }
                    }
                }
            }
            else
            {
                // Reset the flag when not using a recall item
                playerUsedRecall[player.Index] = false;
            }
        }
    }

    private void SetSpawnPoint(CommandArgs args)
    {
        var player = args.Player;
        if (player == null || !player.IsLoggedIn)
        {
            player.SendErrorMessage("You must be logged in to set your spawn point.");
            return;
        }

        SaveAndSetSpawnPoint(player);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TShockAPI.Hooks.PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
        }
        base.Dispose(disposing);
    }
}
