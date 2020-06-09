using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace HighlightDownloader
{
    public enum VideoType
    {
        Upload,
        PastPremiere,
        Archive,
        Highlight,
        All
    }
    public class VideoInfo
    {
        public ulong broadcast_id { get; set; }
        public string title { get; set; }
        public string _id { get; set; }
        public string created_at { get; set; }
        public int views { get; set; }
        public int length { get; set; }
    }
    class Options
    {
        /// <summary>
        /// Auth token
        /// </summary>
        public string Token { get; set; }
    }
    class Program
    {
        static string TwitchClientID = "kimne78kx3ncx6brgo4mv6wki5h1ko";
        static string TwitchToken;
        static string RootPath = Environment.CurrentDirectory;
        const int PAGESIZE = 30;

        static void Main(string[] args)
        {
            var options = GetConfig();
            TwitchToken = options.Token;

            var folder = Path.Combine(RootPath, "downloads");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string line = "";
            int offset = 0;
            VideoType type = VideoType.All;
            List<VideoInfo> videos = GetVideos(type);
            while (!line.StartsWith("q"))
            {
                Console.Clear();
                Console.WriteLine($"Videos page {Math.Ceiling(offset / (double)PAGESIZE) + 1}");
                int i = 1;
                foreach (var vid in videos)
                {
                    Console.WriteLine($"{i++}. [{vid.created_at}] {vid._id}-{vid.title} ({new TimeSpan(0, 0, vid.length):hh\\:mm\\:ss})");
                }
                Console.WriteLine($"Enter 1-{PAGESIZE} to download specific, a to download page, n to get next page, p to get previous page, q to quit, t to change types:");
                line = Console.ReadLine();
                if (int.TryParse(line, out int index) && index > 0 && index <= PAGESIZE)
                {
                    var vid = videos[index - 1];
                    var fileName = SanitizeFile($"[{vid.created_at}] {vid.title}-{vid._id}.ts");
                    var filePath = Path.Combine(folder, fileName);
                    DownloadVideo(vid._id, filePath);
                    Console.WriteLine("Press any key to continue");
                    Console.ReadKey();
                }
                else
                {
                    switch (line)
                    {
                        case "a":
                            foreach (var vid in videos)
                            {
                                var fileName = SanitizeFile($"[{vid.created_at}] {vid.title}-{vid._id}.ts");
                                var filePath = Path.Combine(folder, fileName);
                                DownloadVideo(vid._id, filePath);
                            }
                            Console.WriteLine("Press any key to continue");
                            Console.ReadKey();
                            break;
                        case "n":
                            if (videos.Count >= PAGESIZE)
                            {
                                offset += PAGESIZE;
                                videos = GetVideos(type, offset: offset);
                            }
                            else
                            {
                                Console.WriteLine("No more pages after current. Press any key to continue");
                                Console.ReadKey();
                            }
                            break;
                        case "p":
                            if (offset > 0)
                            {
                                offset -= PAGESIZE;
                                videos = GetVideos(type, offset: offset);
                            }
                            else
                            {
                                Console.WriteLine("No more pages before current. Press any key to continue");
                                Console.ReadKey();
                            }
                            break;
                        case "t":
                            Console.Clear();
                            Console.WriteLine("Select type of videos:");
                            Console.WriteLine("a. All");
                            Console.WriteLine("b. Past Broadcast");
                            Console.WriteLine("h. Highlight");
                            Console.WriteLine("u. Upload");
                            Console.WriteLine("p. Past Premiere");
                            line = Console.ReadLine();
                            type = line == "b" ? VideoType.Archive :
                                line == "h" ? VideoType.Highlight :
                                line == "u" ? VideoType.Upload :
                                line == "p" ? VideoType.PastPremiere :
                                VideoType.All;

                            offset = 0;
                            videos = GetVideos(type);
                            Console.WriteLine("Press any key to continue");
                            Console.ReadKey();
                            break;
                        default:
                            Console.WriteLine("Invalid option. Press any key to continue");
                            Console.ReadKey();
                            break;
                    }
                }
            }
        }

        static Options GetConfig()
        {
            var options = new Options();
            Console.WriteLine("Paste in auth token:");
            options.Token = Console.ReadLine().Trim();

            return options;
        }

        static List<VideoInfo> GetVideos(VideoType type, int limit = 30, int offset = 0)
        {
            var http = GetHttpClient(true);
            string broadcast_type = "";
            switch(type)
            {
                case VideoType.Archive:
                    broadcast_type = "archive";
                    break;
                case VideoType.Highlight:
                    broadcast_type = "highlight";
                    break;
                case VideoType.PastPremiere:
                    broadcast_type = "past_premiere";
                    break;
                case VideoType.Upload:
                    broadcast_type = "upload,premiere_upload";
                    break;
            }
            var url = type == VideoType.All ? 
                $"https://api.twitch.tv/v5/channels/39658500/video_manager?limit={limit}&offset={offset}&status=recorded%2Crecording"
                : $"https://api.twitch.tv/v5/channels/39658500/video_manager?broadcast_type={broadcast_type}&limit={limit}&offset={offset}&status=recorded%2Crecording";
            var result = http.GetAsync(url).GetAwaiter().GetResult();
            var content = result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JToken.Parse(content);
            return json.SelectToken("videos").ToObject<List<VideoInfo>>();
        }

        static void DownloadVideo(string videoId, string filePath)
        {
            var id = videoId.TrimStart('v');

            var http = GetHttpClient();

            // Get Video Access Token
            var url = $"https://api.twitch.tv/api/vods/{id}/access_token?oauth_token={TwitchToken}";
            var result = http.GetAsync(url).GetAwaiter().GetResult();
            var content = result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JToken.Parse(content);

            // Get m3u8 
            url = $"https://usher.ttvnw.net/vod/{id}.m3u8?sig={json.SelectToken("sig")}&token={json.SelectToken("token")}";
            result = http.GetAsync(url).GetAwaiter().GetResult();
            content = result.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            // Get first video quality option
            using var sr = new StringReader(content);
            while (sr.Peek() != -1)
            {
                string line = sr.ReadLine();
                if (!line.StartsWith('#'))
                {
                    url = line;
                    result = http.GetAsync(url).GetAwaiter().GetResult();
                    content = result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    break;
                }
            }

            // Download stream into one file
            var uri = new Uri(url);

            var baseUrl = $"{uri.Scheme}://{uri.Host}{string.Join("", uri.Segments.Take(uri.Segments.Length - 1))}";
            Console.WriteLine($"Downloading {Path.GetFileNameWithoutExtension(filePath)}...");

            if (File.Exists(filePath)) File.Delete(filePath);
            using var fsr = new StringReader(content);
            while (fsr.Peek() != -1)
            {
                string line = fsr.ReadLine();
                if (!line.StartsWith('#'))
                {
                    url = Path.Combine(baseUrl, line);
                    result = http.GetAsync(url).GetAwaiter().GetResult();

                    var stream = result.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    var fs = File.Open(filePath, FileMode.Append);
                    stream.CopyTo(fs);
                    fs.Close();

                    Console.WriteLine($"Downloaded {stream.Position} bytes...");
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                }
            }
            Console.WriteLine($"Download Complete");
        }

        static string SanitizeFile(string origFileName)
        {
            var invalids = Path.GetInvalidFileNameChars();
            return string.Join("_", origFileName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        static HttpClient GetHttpClient(bool authorize = false)
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            if (authorize)
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", TwitchToken);
            return http;
        }
    }
}
