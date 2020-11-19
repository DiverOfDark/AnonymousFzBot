using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AnonymousFzBot
{
    internal class State : IDisposable
    {
        static State()
        {
            var stateFile = Environment.GetEnvironmentVariable("statefile");
            if (!string.IsNullOrWhiteSpace(stateFile))
            {
                StateFile = stateFile;
            }
        }

        private static string StateFile = "state.json";
        
        public void Dispose()
        {
        }
        
        public Dictionary<long, Dictionary<int, int>> ForwardedMessageIds { get; set; } = new Dictionary<long, Dictionary<int, int>>(); // ChatId of user -> OriginalMessageId -> ForwardedMessageId  

        public Dictionary<int, List<int>> UserMessages { get; set; } = new Dictionary<int, List<int>>(); // user messages
        
        public Dictionary<int, long> EnabledUsers { get; set; } = new Dictionary<int, long>(); // User -> ChatId
        public List<int> BannedUsers { get; set; } = new List<int>();

        public static State Load()
        {
            if (File.Exists(StateFile))
            {
                return JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile));
            }
            return new State();
        }

        public void Save()
        {
            File.WriteAllText(StateFile, JsonSerializer.Serialize(this));
        }
    }
}