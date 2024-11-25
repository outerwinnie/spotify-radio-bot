using Discord;
using Discord.WebSocket;
using SpotifyAPI.Web;

namespace spotify_radio_bot
{
    class Program
    {
        private DiscordSocketClient _client;
        private SpotifyClient _spotifyClient;
        private int _playlistCap;
        private ulong _channelId;

        static async Task Main(string[] args)
        {
            var program = new Program();
            await program.RunBotAsync();
        }

        public async Task RunBotAsync()
        {
            // Initialize Discord bot
            _client = new DiscordSocketClient();
            _client.Log += LogAsync;
            _client.MessageReceived += MessageReceivedAsync;

            // Login Discord bot
            var discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            if (string.IsNullOrEmpty(discordToken))
                throw new Exception("DISCORD_BOT_TOKEN environment variable is not set.");

            await _client.LoginAsync(TokenType.Bot, discordToken);
            await _client.StartAsync();

            // Initialize Spotify client
            var spotifyConfig = SpotifyClientConfig.CreateDefault()
                .WithAuthenticator(new ClientCredentialsAuthenticator(
                    Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID"),
                    Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET")));

            _spotifyClient = new SpotifyClient(spotifyConfig);

            // Get playlist cap from environment variables
            var playlistCapStr = Environment.GetEnvironmentVariable("SPOTIFY_PLAYLIST_CAP");
            _playlistCap = string.IsNullOrEmpty(playlistCapStr) ? 100 : int.Parse(playlistCapStr);

            // Get channel ID from environment variables
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            if (string.IsNullOrEmpty(channelIdStr))
                throw new Exception("DISCORD_CHANNEL_ID environment variable is not set.");
            _channelId = ulong.Parse(channelIdStr);

            Console.WriteLine($"Playlist cap set to {_playlistCap} songs.");
            Console.WriteLine($"Bot will monitor channel ID: {_channelId}");

            await Task.Delay(-1); // Keep bot running
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            // Ignore bot messages and messages from other channels
            if (message.Author.IsBot || message.Channel.Id != _channelId) return;

            // Look for Spotify links
            var spotifyUrl = message.Content;
            if (spotifyUrl.Contains("open.spotify.com/track"))
            {
                await message.Channel.SendMessageAsync("Spotify link detected! Checking playlist...");

                var trackId = ExtractSpotifyId(spotifyUrl);

                if (!string.IsNullOrEmpty(trackId))
                {
                    // Get playlist details
                    var playlistId = Environment.GetEnvironmentVariable("SPOTIFY_PLAYLIST_ID");
                    if (string.IsNullOrEmpty(playlistId))
                    {
                        await message.Channel.SendMessageAsync("Playlist ID environment variable is not set.");
                        return;
                    }

                    var playlist = await _spotifyClient.Playlists.Get(playlistId);

                    // Check playlist size and remove the oldest track if necessary
                    if (playlist.Tracks.Total >= _playlistCap)
                    {
                        var oldestTrack = playlist.Tracks.Items.FirstOrDefault();
                        if (oldestTrack?.Track is FullTrack fullTrack)
                        {
                            try
                            {
                                await _spotifyClient.Playlists.RemoveItems(playlistId, new PlaylistRemoveItemsRequest
                                {
                                    Tracks = new[]
                                    {
                                        new PlaylistRemoveItemsRequest.Item { Uri = fullTrack.Uri }
                                    }
                                });
                                await message.Channel.SendMessageAsync($"Removed the oldest track: {fullTrack.Name}.");
                            }
                            catch (Exception ex)
                            {
                                await message.Channel.SendMessageAsync($"Error removing the oldest track: {ex.Message}");
                                return;
                            }
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync("Could not identify the oldest track to remove.");
                            return;
                        }
                    }

                    // Add the new track
                    try
                    {
                        await _spotifyClient.Playlists.AddItems(playlistId, new PlaylistAddItemsRequest(new[] { $"spotify:track:{trackId}" }));
                        await message.Channel.SendMessageAsync("Track successfully added to the playlist!");
                    }
                    catch (Exception ex)
                    {
                        await message.Channel.SendMessageAsync($"Error adding track: {ex.Message}");
                    }
                }
                else
                {
                    await message.Channel.SendMessageAsync("Could not extract track ID from the link.");
                }
            }
        }

        private string ExtractSpotifyId(string url)
        {
            // Extract the Spotify track ID from the URL
            try
            {
                var parts = url.Split(new[] { '/', '?' }, StringSplitOptions.RemoveEmptyEntries);
                var trackIndex = Array.IndexOf(parts, "track") + 1;
                return trackIndex < parts.Length ? parts[trackIndex] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
