using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SpotifyAPI.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
class Program
{
    private readonly DiscordSocketClient _client;
    private readonly string _discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
    private readonly string _spotifyClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
    private readonly string _spotifyClientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");
    private readonly string _spotifyPlaylistId = Environment.GetEnvironmentVariable("SPOTIFY_PLAYLIST_ID");
    private readonly string _redirectUri = Environment.GetEnvironmentVariable("REDIRECT_URI");
    private readonly ulong _channelId = Convert.ToUInt64(Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID"));
    private readonly int _playlistCap = Convert.ToInt32(Environment.GetEnvironmentVariable("SPOTIFY_PLAYLIST_CAP"));

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
                Console.WriteLine($"Track ID: {trackId}");
                await AddTrackToSpotifyPlaylistAsync(trackId);
            }
        }
    }

    private string DetectSpotifyTrackLink(string message)
    {
        // Updated regex to handle both regional and standard Spotify links
        string pattern = @"https:\/\/open\.spotify\.com\/(?:intl-[a-z]{2}\/)?track\/[a-zA-Z0-9]+";
        Match match = Regex.Match(message, pattern);
        return match.Success ? match.Value : null;
    }

    private string ExtractTrackId(string spotifyLink)
    {
        // Updated regex to capture track ID from both formats
        string pattern = @"https:\/\/open\.spotify\.com\/(?:intl-[a-z]{2}\/)?track\/([a-zA-Z0-9]+)";
        Match match = Regex.Match(spotifyLink, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task InitializeSpotifyClientAsync()
    {
        // Step 1: Generate the authorization URL for user authentication
        var loginRequest = new LoginRequest(new Uri(_redirectUri), _spotifyClientId, LoginRequest.ResponseType.Code)
        {
            Scope = new[] { "playlist-modify-public", "playlist-modify-private" }
        };

        var uri = loginRequest.ToUri();
        Console.WriteLine("Go to this URL and authorize the application:");
        Console.WriteLine(uri);

        // Step 2: Start the local server to listen for the callback
        var authorizationCode = await GetAuthorizationCodeAsync();

        // Step 3: Exchange the authorization code for an access token
        var tokenRequest = new AuthorizationCodeTokenRequest(_spotifyClientId, _spotifyClientSecret, authorizationCode, new Uri(_redirectUri));
        var tokenResponse = await new OAuthClient().RequestToken(tokenRequest);

        var accessToken = tokenResponse.AccessToken;

        // Step 4: Initialize the Spotify client with the obtained access token
        var spotifyConfig = SpotifyClientConfig
            .CreateDefault()
            .WithToken(accessToken);  // Use WithToken to directly pass the access token

        _spotifyClient = new SpotifyClient(spotifyConfig);
    }
    
    private async Task<string> GetAuthorizationCodeAsync()
    {
        string code = null;

        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        // Define the route to handle the Spotify callback
        app.MapGet("/callback", (HttpContext context) =>
        {
            // Capture the authorization code
            code = context.Request.Query["code"];
        
            if (!string.IsNullOrEmpty(code))
            {
                Console.WriteLine($"Authorization code received: {code}");
            }
            else
            {
                Console.WriteLine("Authorization code not found in the request.");
            }

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync("<html><body><h1>Authorization complete. You can close this window.</h1></body></html>");
        });

        // Start the server on port 5028
        var serverUrl = "http://0.0.0.0:5028";
        var appTask = app.RunAsync(serverUrl);

        Console.WriteLine($"Listening for Spotify callback on {serverUrl}...");

        // Wait for the code to be received
        var timeout = Task.Delay(TimeSpan.FromMinutes(5)); // Optional timeout for waiting
        while (code == null)
        {
            if (timeout.IsCompleted)
            {
                Console.WriteLine("Timeout waiting for authorization code.");
                await app.StopAsync();
                throw new TimeoutException("Spotify authorization timed out.");
            }

            await Task.Delay(500); // Poll every 500ms
        }

        // Stop the server once the code is received
        await app.StopAsync();
        return code;
    }


    private async Task AddTrackToSpotifyPlaylistAsync(string trackId)
{
    try
    {
        // Ensure _spotifyClient is initialized
        if (_spotifyClient == null)
        {
            Console.WriteLine("Spotify client is not initialized.");
            return;
        }

        // Get existing tracks in the playlist
        var playlist = await _spotifyClient.Playlists.Get(_spotifyPlaylistId);

        if (playlist == null || playlist.Tracks == null || playlist.Tracks.Items == null)
        {
            Console.WriteLine("Error: Unable to retrieve playlist tracks.");
            return;
        }

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
