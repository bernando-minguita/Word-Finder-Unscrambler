using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace WordFinder.NETFramework
{
    public partial class Form1 : Form
    {
        // HttpClient is intended to be instantiated once and re-used throughout the life of an application.
        private static readonly HttpClient _httpClient = new HttpClient();

        // Holds the full result set in memory to allow instant filtering without new API calls.
        private WordApiResponse _currentData;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        /// <summary>
        /// Execution logic for the search button. 
        /// Retrieves words from the API based on the letters and optional length filter.
        /// </summary>
        private async void BtnSearch_Click(object sender, EventArgs e)
        {
            string letters = txtLetters.Text.Trim();
            int selectedLength = (int)numericUpDown1.Value;

            if (string.IsNullOrEmpty(letters))
            {
                MessageBox.Show("Please enter letters to begin your search.", "Input Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Lock UI and provide feedback
            btnSearch.Enabled = false;
            toolStripStatusResultsCount.Text = "Searching...";
            listView1.Items.Clear();

            try
            {
                // Step 1: Call the remote API service
                string jsonResult = await GetRawWordsAsync(letters, selectedLength);

                // Step 2: Deserialize JSON into our C# class structure
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _currentData = JsonSerializer.Deserialize<WordApiResponse>(jsonResult, options);

                if (_currentData != null)
                {
                    // Step 3: Update metadata display
                    toolStripStatusResultsCount.Text = $"Total Results: {_currentData.SearchResults}";

                    // Step 4: Populate the UI list
                    if (_currentData.WordPages != null)
                    {
                        //DisplayResults(_currentData.WordPages);
                        TextFilter_TextChanged(null, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                toolStripStatusResultsCount.Text = "Error";
                MessageBox.Show($"Search failed: {ex.Message}", "API Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnSearch.Enabled = true;
            }
        }

        /// <summary>
        /// Real-time filtering logic. 
        /// Filters the existing cached results based on characters contained in the word.
        /// </summary>
        private void TextFilter_TextChanged(object sender, EventArgs e)
        {
            // Ensure we have data to filter
            if (_currentData == null || _currentData.WordPages == null) return;

            // Get values from all three filter controls
            string filterText = textFilter.Text.ToLower().Trim();
            string startsWith = textStarts.Text.ToLower().Trim();
            string endsWith = textEnds.Text.ToLower().Trim();

            // Create a temporary view model filtered by all three criteria
            var filteredPages = _currentData.WordPages
                .Select(p => new WordPage
                {
                    Length = p.Length,
                    WordList = p.WordList.Where(w =>
                        // Condition 1: Word contains the general filter text
                        (string.IsNullOrEmpty(filterText) || w.Word.ToLower().Contains(filterText)) &&
                        // Condition 2: Word starts with the 'txtStarts' text
                        (string.IsNullOrEmpty(startsWith) || w.Word.ToLower().StartsWith(startsWith)) &&
                        // Condition 3: Word ends with the 'txtEnds' text
                        (string.IsNullOrEmpty(endsWith) || w.Word.ToLower().EndsWith(endsWith))
                    ).ToList()
                })
                .Where(p => p.WordList.Count > 0) // Only include pages that still have words
                .ToList();

            // Refresh the ListView with the combined results
            DisplayResults(filteredPages);
        }

        /// <summary>
        /// Event listener to trigger search when the user presses Enter in the input field.
        /// </summary>
        private void TxtLetters_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnSearch.PerformClick();
                e.SuppressKeyPress = true; // Prevents the Windows error sound
            }
        }

        /// <summary>
        /// Updates the ListView control with word data.
        /// </summary>
        private void DisplayResults(List<WordPage> pages)
        {
            if (pages == null) return;

            listView1.BeginUpdate();
            listView1.Items.Clear();

            int visibleCount = 0;
            foreach (var page in pages.OrderByDescending(p => p.Length))
            {
                if (page.WordList == null) continue;

                foreach (var entry in page.WordList)
                {
                    ListViewItem item = new ListViewItem(entry.Word);
                    item.SubItems.Add(page.Length.ToString());
                    listView1.Items.Add(item);
                    visibleCount++;
                }
            }

            listView1.EndUpdate();

            // Update label to show filtered count vs total
            if (_currentData != null)
            {
                toolStripStatusResultsCount.Text = $"Showing {visibleCount} of {_currentData.SearchResults} results";
            }
        }

        /// <summary>
        /// Core networking logic to communicate with the 'fly.wordfinderapi.com' API.
        /// </summary>
        public async Task<string> GetRawWordsAsync(string letters, int length)
        {
            var builder = new UriBuilder("https://fly.wordfinderapi.com/api/search");
            var query = HttpUtility.ParseQueryString(string.Empty);

            query["letters"] = letters;
            query["dictionary"] = "all_en";
            query["word_sorting"] = "points";
            query["group_by_length"] = "true";
            query["page_size"] = "500";

            if (length > 0) query["length"] = length.ToString();

            builder.Query = query.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, builder.ToString());

            // Adding Headers to simulate a legitimate browser request
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36");
            request.Headers.Add("Origin", "https://word.tips");
            request.Headers.Add("Referer", "https://word.tips/");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }    
    }

    #region Data Models
    /// <summary>
    /// Represents the top-level response from the WordFinder API.
    /// </summary>
    public class WordApiResponse
    {
        [JsonPropertyName("search_results")]
        public int SearchResults { get; set; }

        [JsonPropertyName("word_pages")]
        public List<WordPage> WordPages { get; set; }
    }

    /// <summary>
    /// Represents a group of words categorized by their character count.
    /// </summary>
    public class WordPage
    {
        [JsonPropertyName("word_list")]
        public List<WordEntry> WordList { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }
    }

    /// <summary>
    /// Represents an individual word entry.
    /// </summary>
    public class WordEntry
    {
        [JsonPropertyName("word")]
        public string Word { get; set; }
    }
    #endregion

}