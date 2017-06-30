/* This program was created to be used once, and as such contains a lot of procedual mess, poor coding habits, and generally rushed and therefore unstable code.
If you see this online, just keep the latter in mind when reading through.

Created by Jordan Mele in 2017, code has no license and free to be used any way you see fit. Just don't do doing anything nefarious.
Huge thanks to the creators of Humanizer, WikiClientLibrary, and Newtonsoft.Json, with which I wouldn't have been able to throw this together without.
 */
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using Humanizer;
using WikiClientLibrary;
using WikiClientLibrary.Client;

namespace Grimoire_Gen
{
    class Program
    {
        public static void Main(string[] args) {
            MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task MainAsync(string[] args) {
            Console.WriteLine("Starting...");

            // Set up bungie client and add key
            HttpClient bungieClient = new HttpClient();
            bungieClient.DefaultRequestHeaders.Add("X-API-KEY", "");
            bungieClient.BaseAddress = new Uri("http://www.bungie.net/");
            string payload;

            // Set up wiki client for later use
            WikiClient wikiClient = new WikiClient() { ClientUserAgent = "GrimoireGenBot/1.0", ThrottleTime = new TimeSpan(0, 0, 1) };

            Site destinyWiki = await Site.CreateAsync(wikiClient, "https://destiny.wikia.com/api.php");
            await destinyWiki.LoginAsync("Silicon Soldier Bot", "");

            try {
                // Grab Grimoire, but don't dispose so we can grab the images later.
                payload = await bungieClient.GetStringAsync("Platform/Destiny/Vanguard/Grimoire/Definition/");
            }
            catch (HttpRequestException e) {
                // Things didn't work, so we announce it.
                WriteColoredLine("Damn... A HTTP error! Couldn't get Grimoire definitions./n" + e.Message, ConsoleColor.Red);
                return;
            }

            // Abort if payload is empty.
            if (payload == "") {
                WriteColoredLine("Payload is empty, aborting.", ConsoleColor.Red);
                return;
            }
            else {
                WriteColoredLine("Payload recieved.", ConsoleColor.Green);
            }

            ThemeCollection[] tcs = JsonConvert.DeserializeObject<GrimoireDefinitions>(payload).Response.ThemeCollection;

            List<Tuple<string, bool?>> cardResults = new List<Tuple<string, bool?>>();
            foreach (ThemeCollection tc in tcs) {
                foreach (PageCollection pc in tc.PageCollection) {
                    string category = pc.PageName.Singularize() + " Grimoire Cards";
                    foreach (GrimoireCard cc in pc.GrimoireCard) {
                        cardResults.Add(await CreateGrimoireCardArticleAsync(destinyWiki, bungieClient, cc, category));
                    }
                }
            }

            // Recover what resources we can
            bungieClient.Dispose();
            await destinyWiki.LogoutAsync();

            // Log results
            string success;
            string skip;
            string fail;
            success = skip = fail = "";

            foreach (Tuple<string, bool?> result in cardResults) {
                switch (result.Item2) {
                    case true:
                        success += result.Item1 + "\n";
                        break;
                    case null:
                        skip += result.Item1 + "\n";
                        break;
                    case false:
                        fail += result.Item1 + "\n";
                        break;
                }
            }

            try {
                System.IO.File.WriteAllText($@".\Grimoire\Results.txt", $"Success\n=====\n{success}\nSkipped\n=====\n{skip}\nFailed\n=====\n{fail}");
            }
            catch (UnauthorizedAccessException e) {
                // Permission error. Report and return.
                WriteColoredLine($"Not allowed to save results.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return;
            }
            catch (IOException e) {
                // Permission error. Report and return.
                WriteColoredLine($"Failed to save results.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return;
            }
            catch (Exception e) {
                // Unknown error. Report and return.
                WriteColoredLine($"Disaster! An unexpected exception was thrown while storing the results.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return;
            }

            WriteColoredLine("Complete", ConsoleColor.Green);

            return;
        }

        private static void WriteColoredLine(string message, ConsoleColor color) {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        /// <summary>Replaces HTML tags with their wikitext equivilants.</summary>
        /// <param name="content">Content to convert</param>
        private static string HtmlToWikitext(string content) {
            if (content == null) {
                return "";
            }
            // Decode entities
            content = System.Net.WebUtility.HtmlDecode(content);
            // Bold tag
            content = content.Replace("<b>", "'''");
            content = content.Replace("</b>", "'''");
            // Italics tag
            content = content.Replace("<i>", "''");
            content = content.Replace("</i>", "'");
            // Line break tag
            //content = content.Replace("<br/>", "\n");// Double needed to force Media Wiki to acknowledge implied line break
            return content;
        }

        /// <summary>
        /// Downloads card image, generates image page content, and generates article content. If page is found to not yet exist on the wiki (with certainty), 
        /// </summary>
        /// <param name="site">Site to attempt to publish page at.</param>
        /// <param name="bungieClient">Client used to connect to bungie API.</params>
        /// <param name="card">Grimoire Card from Bungie.net to create article from.</param>
        /// <param name="category">Processed category to be appended. Excludes category namespace, etc.</param>
        /// <returns>Tuple containing card name and success state. If success state is null, upload was skipped due to article already existing.</returns>
        private static async Task<Tuple<string, bool?>> CreateGrimoireCardArticleAsync(Site site, HttpClient bungieClient, GrimoireCard card, string category) {
            // HTML decode card name
            card.CardName = System.Net.WebUtility.HtmlDecode(card.CardName);
            // Eliminate any \n
            card.CardName = card.CardName.Replace("\n", "");

            // Produce required escaped names.
            string fsCardName, mwCardName;
            StringBuilder sb = new StringBuilder();
            // File system friendly name
            foreach (char c in card.CardName) {
                if (System.IO.Path.GetInvalidFileNameChars().Contains(c)) {
                    sb.Append('-');
                }
                else {
                    sb.Append(c);
                }
            }
            fsCardName = sb.ToString();
            sb.Clear();
            // MediaWiki friendly name
            foreach (char c in card.CardName) {
                if ("#<>[]|{}&?".Contains(c)) {
                    sb.Append('-');
                }
                else {
                    sb.Append(c);
                }
            }
            mwCardName = sb.ToString();
            sb.Clear();

            // Create directory for card
            Directory.CreateDirectory($@".\Grimoire\{fsCardName}");

            // Grab and store image
            try {
                // Get image
                HttpResponseMessage response;
                lock (bungieClient) {
                    response = bungieClient.GetAsync(card.HighResolution.Image.SheetPath).GetAwaiter().GetResult();
                }
                response.EnsureSuccessStatusCode();
                // Save image
                using (FileStream fs = new FileStream($@".\Grimoire\{fsCardName}\Grimoire Card.jpg", FileMode.Create, FileAccess.Write, FileShare.None)) {
                    await response.Content.CopyToAsync(fs);
                }
                //sometimes this doesn't close before its needed later
            }
            catch (HttpRequestException e) {
                // HTTP error. Report and return.
                WriteColoredLine($"{card.CardName}: Failed to download card image.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (UnauthorizedAccessException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Not allowed to save downloaded image.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (IOException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Failed to save downloaded image.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (Exception e) {
                // Unknown error. Report and return.
                WriteColoredLine($"{card.CardName}: Disaster! An unexpected exception was thrown while grabbing and storing the card image.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }

            // Generate and store image description
            string desc = "{{Image summary\n|type=Graphic\n";
                    desc += $"|description={card.CardName}Grimoire Card\n|source=[{bungieClient.BaseAddress}{card.HighResolution.Image.SheetPath} Bungie.net]\n";
                    desc += "|holder=Bungie\n|license={{Fair Use}}\n}}\n[[Category:Grimoire Card Images]]\n[[Category:Graphics]]\n[[Category:Destiny Images]]";// Ideally, a image recognition library would be used to determine the game the grimoire card comes from. Tad resource intensive, but would pay off. Sadly time constraints mean this one-time scenario won't use this.
            try {
                System.IO.File.WriteAllText($@".\Grimoire\{fsCardName}\Image Description.txt", desc);
            }
            catch (UnauthorizedAccessException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Not allowed to save image description.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (IOException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Failed to save image description.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (Exception e) {
                // Unknown error. Report and return.
                WriteColoredLine($"{card.CardName}: Disaster! An unexpected exception was thrown while storing the image description.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }

            // Generate and store article
            string article;
            // Title
            article = $"{{{{DISPLAYTITLE:{card.CardName}}}}}";// No new line to prevent wiki creating line break
            // Infobox
            article += $"{{{{Infobox/GrimoireCard\n|image={fsCardName} Grimoire Card.jpg\n|name={card.CardName}\n|unlock=\n|grimoire=";
            if (card.Points == 0) {
                article += "0\n}}";// No new line to prevent wiki creating line break
            }
            else {
                article += $"+{card.Points}\n}}}}";// No new line to prevent wiki creating line break
            }
            // Quote
            article += "{{Quote|";
            card.CardIntro = HtmlToWikitext(card.CardIntro);
            card.CardIntroAttribution = HtmlToWikitext(card.CardIntroAttribution);
            card.CardDescription = HtmlToWikitext(card.CardDescription);
            if (card.CardIntro != "") {// Handle intro if exists
                article += card.CardIntro;
                if (card.CardIntroAttribution != "") {// Handle intro attribution if exists
                    article += $" {card.CardIntroAttribution}";
                }
                article += "\n\n";
            }
            article += $"{card.CardDescription}}}}}\n\n";
            // References block
            article += "==References==\n<references/>\n";
            // Categories
            article += $"[[Category:{category}]]";
            // Store
            try {
                System.IO.File.WriteAllText($@".\Grimoire\{fsCardName}\Article.txt", article);
            }
            catch (UnauthorizedAccessException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Not allowed to save article.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (IOException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Failed to save article.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (Exception e) {
                // Unknown error. Report and return.
                WriteColoredLine($"{card.CardName}: Disaster! An unexpected exception was thrown while storing the article.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }

            // If MediaWiki friendly name is different, cannot automatically make article.
            if (card.CardName != mwCardName) {
                WriteColoredLine($"{card.CardName}: MediaWiki friendly name is different from card name. Cannot reliably check article existance. Skipping upload.", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }

            // Generate page object
            Page articlePage = new Page(site, $"List of Grimoire Cards/{mwCardName}");

            // Grab page.
            lock (site) {
                articlePage.RefreshAsync().GetAwaiter().GetResult();
            }

            // Check page existance
            if (articlePage.Exists) {
                WriteColoredLine($"{card.CardName}: Article already exists. Skipping upload.", ConsoleColor.Blue);
                return new Tuple<string, bool?>(card.CardName, null);
            }

            // We got this far... Time for upload!

            // Image
            try {
                using (FileStream fs = new FileStream($@".\Grimoire\{fsCardName}\Grimoire Card.jpg", FileMode.Open, FileAccess.Read, FileShare.None)) {
                    lock (site) {
                        FilePage.UploadAsync(site, fs, $"File:{fsCardName} Grimoire Card.jpg", desc, true).GetAwaiter().GetResult();
                        Thread.Sleep(1000);
                    }
                }
            }
            catch (UnauthorizedAccessException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Not allowed to access image.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (IOException e) {
                // Permission error. Report and return.
                WriteColoredLine($"{card.CardName}: Failed to access image.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }
            catch (Exception e) {
                try {
                    Thread.Sleep(5000);// Wait 5 seconds to allow network to recover if needed
                    using (FileStream fs = new FileStream($@".\Grimoire\{fsCardName}\Grimoire Card.jpg", FileMode.Open, FileAccess.Read, FileShare.None)) {
                    lock (site) {
                        FilePage.UploadAsync(site, fs, $"File:{fsCardName} Grimoire Card.jpg", desc, true).GetAwaiter().GetResult();
                        Thread.Sleep(1000);
                    }
                }
                }
                catch {
                    WriteColoredLine("We tried to upload twice, to no avail.", ConsoleColor.Red);
                    // Unknown error. Report and return.
                    WriteColoredLine($"{card.CardName}: Disaster! An unexpected exception was thrown while uploading the image.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                    return new Tuple<string, bool?>(card.CardName, false);
                }
            }

            // Article
            articlePage.Content = article;
            try {
                lock (site) {// Lock to prevent parallel requests
                    articlePage.UpdateContentAsync("Article automatically generated using data from Bungie.net API.").GetAwaiter().GetResult();
                }
            }
            catch (Exception e) {
                // Unknown error. Report and return.
                WriteColoredLine($"{card.CardName}: Disaster! An unexpected exception was thrown while uploading the article.\nMessage: {e.Message}\nStack Trace: {e.StackTrace}\nHelp link: {e.HelpLink}", ConsoleColor.Red);
                return new Tuple<string, bool?>(card.CardName, false);
            }

            WriteColoredLine($"{card.CardName}: Complete", ConsoleColor.Green);
            return new Tuple<string, bool?>(card.CardName, true);
        }
    }
}
