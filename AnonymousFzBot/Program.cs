using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;

namespace AnonymousFzBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var botClient = new TelegramBotClient(args[0]);
            var state = State.Load();
            using var worker = new RedirectorBot(botClient, state);
            while (true)
            {
                Thread.Sleep(3000);
                state.Save();
            }
        }
    }
}