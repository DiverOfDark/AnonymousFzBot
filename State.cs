using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Json;
using Newtonsoft.Json;

namespace AnonymousFzBot
{
    internal class State : IDisposable
    {
        public void Dispose()
        {
        }
        
        public Dictionary<long, Dictionary<int, int>> ForwardedMessageIds { get; } = new Dictionary<long, Dictionary<int, int>>(); // ChatId of user -> OriginalMessageId -> ForwardedMessageId  

        public Dictionary<int, long> EnabledUsers { get; } = new Dictionary<int, long>(); // User -> ChatId
        public List<int> BannedUsers { get; } = new List<int>();

        public static State Load()
        {
            if (File.Exists("state.json"))
            {
                return JsonConvert.DeserializeObject<State>("state.json");
            }
            return new State();
        }

        public void Save()
        {
            File.WriteAllText("state.json", JsonConvert.SerializeObject(this));
        }
    }
}