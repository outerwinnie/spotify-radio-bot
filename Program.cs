using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using SpotifyAPI.Web;
using System.Collections.Generic;

class Program
{
    private readonly DiscordSocketClient _client;
    private readonly string _discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? throw new InvalidOperationException();
    private readonly string _spotifyClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? throw new InvalidOperationException();
    private readonly string _spotifyClientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET") ?? throw new InvalidOperationException();
    private readonly string _spotifyPlaylistId = Environment.GetEnvironmentVariable("SPOTIFY_PLAYLIST_ID") ?? throw new InvalidOperationException();
    private readonly ulong _channelId = ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID") ?? "0");
    private readonly int _playlistCap = int.Parse(Environment.GetEnvironmentVariable("SPOTIFY_PLAYLIST_CAP") ?? "50");

    private SpotifyClient _spotifyClient;

    public static async Task Main(string[] args) => await new Program().MainAsync();

    public Program()
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };
        _client = new DiscordSocketClient(config);
    }

    public async Task MainAsync()
    {
        _client.Log += LogAsync;
        _client.MessageReceived += MessageReceivedAsync;

        await _client.LoginAsync(TokenType.Bot, _discordToken);
        await _client.StartAsync();

        // Initialize Spotify API
        await InitializeSpotifyClientAsync();

        await Task.Delay(-1);
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        // Ensure the message is from the target channel
        if (message.Channel.Id != _channelId) return;

        // Extract Spotify track link
        string spotifyLink = DetectSpotifyTrackLink(message.Content);
        if (spotifyLink != null)
        {
            Console.WriteLine($"Spotify track link detected: {spotifyLink}");
            string trackId = ExtractTrackId(spotifyLink);

            // Add to Spotify playlist
            if (!string.IsNullOrEmpty(trackId))
            {
                await AddTrackToSpotifyPlaylistAsync(trackId);
            }
        }
    }

    private string DetectSpotifyTrackLink(string message)
    {
        string pattern = @"https?:\/\/open\.spotify\.com\/track\/[a-zA-Z0-9]+";
        Match match = Regex.Match(message, pattern);
        return match.Success ? match.Value : null;
    }

    private string ExtractTrackId(string spotifyLink)
    {
        string pattern = @"https?:\/\/open\.spotify\.com\/track\/([a-zA-Z0-9]+)";
        Match match = Regex.Match(spotifyLink, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task InitializeSpotifyClientAsync()
    {
        var config = SpotifyClientConfig.CreateDefault();
        var request = new ClientCredentialsRequest(_spotifyClientId, _spotifyClientSecret);
        var response = await new OAuthClient(config).RequestToken(request);

        _spotifyClient = new SpotifyClient(config.WithToken(response.AccessToken));
        Console.WriteLine("Spotify client initialized.");
    }

    private async Task AddTrackToSpotifyPlaylistAsync(string trackId)
    {
        try
        {
            // Get existing tracks in the playlist
            var playlist = await _spotifyClient.Playlists.Get(_spotifyPlaylistId);
            var currentTracks = playlist.Tracks.Items;
            var currentTrackUris = new List<string>();

            foreach (var item in currentTracks)
            {
                if (item.Track is FullTrack track)
                    currentTrackUris.Add(track.Uri);
            }

            // Check if track is already in the playlist
            string newTrackUri = $"spotify:track:{trackId}";
            if (currentTrackUris.Contains(newTrackUri))
            {
                Console.WriteLine("Track is already in the playlist.");
                return;
            }

            // Check if we need to remove the oldest track
            if (currentTrackUris.Count >= _playlistCap)
            {
                Console.WriteLine("Playlist cap reached. Removing oldest track.");
                await _spotifyClient.Playlists.RemoveItems(_spotifyPlaylistId, new PlaylistRemoveItemsRequest
                {
                    Tracks = new List<PlaylistRemoveItemsRequest.Item>
                    {
                        new PlaylistRemoveItemsRequest.Item { Uri = currentTrackUris[0] }
                    }
                });

                currentTrackUris.RemoveAt(0);
            }

            // Add the new track
            await _spotifyClient.Playlists.AddItems(_spotifyPlaylistId, new PlaylistAddItemsRequest(new List<string> { newTrackUri }));
            Console.WriteLine("Track added to Spotify playlist.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding track to Spotify playlist: {ex.Message}");
        }
    }
}
