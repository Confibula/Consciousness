using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class GeminiService : MonoBehaviour
{
    private const string API_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-preview-05-20:generateContent?key=YOUR_API_KEY";
    private static readonly HttpClient httpClient = new HttpClient();

    // The GeminiResponse classes remain the same for parsing the JSON.
    [System.Serializable]
    public class GeminiResponse
    {
        public Candidate[] candidates;
    }

    [System.Serializable]
    public class Candidate
    {
        public Content content;
    }

    [System.Serializable]
    public class Content
    {
        public Part[] parts;
    }

    [System.Serializable]
    public class Part
    {
        public string text;
    }

    // Your main method to talk to the model, now using HttpClient.
    public async Task<string> GetRawResponse(string prompt)
    {
        // Construct the payload JSON.
        string payload = $"{{\"contents\":[{{\"parts\":[{{\"text\":\"{prompt}\"}}]}}]}}";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            // Send the request and wait for the response.
            HttpResponseMessage response = await httpClient.PostAsync(API_URL, content);

            // Ensure the request was successful.
            response.EnsureSuccessStatusCode();

            // Read the JSON response as a string.
            string jsonResponse = await response.Content.ReadAsStringAsync();

            return jsonResponse;
        }
        catch (HttpRequestException e)
        {
            Debug.LogError($"Gemini API Error: {e.Message}");
            return null;
        }
    }
}
