using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;

namespace AnonymousFzBot
{
    internal class RedirectorBot : IDisposable
    {
        private readonly TelegramBotClient _botClient;
        private readonly State _state;

        public RedirectorBot(TelegramBotClient botClient, State state)
        {
            _botClient = botClient;
            _state = state;
            _botClient.StartReceiving();
            _botClient.OnMessage += OnMessage;
            _botClient.OnMessageEdited += OnMessageEdited;
        }

        private static bool IsSentByAdmin(MessageEventArgs e) => e.Message.From.Username == "diverofdark" || e.Message.From.Username == "IgorMasko";

        private async Task SafeExecute(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await _botClient.SendTextMessageAsync("diverofdark", "<b>Exception: <b/>" + ex, ParseMode.Html, disableNotification: false);
            }
        }

        private (int originalMessageId, bool sentByMe) GetProxiedMessageOriginalId(int receivedByUserId, int proxiedMessageId)
        {
            // 0 - no replyTo / noId
            if (proxiedMessageId == 0)
                return (0, false);
            
            if (!_state.UserMessages.TryGetValue(receivedByUserId, out var myMessages))
            {
                _state.UserMessages[receivedByUserId] = myMessages = new List<int>();
            }

            if (myMessages.Contains(proxiedMessageId))
            {
                return (proxiedMessageId, true); // was sent by me;
            }

            if (!_state.ForwardedMessageIds.TryGetValue(receivedByUserId, out var proxiedForMe))
            {
                _state.ForwardedMessageIds[receivedByUserId] = proxiedForMe = new Dictionary<int, int>();
            }

            return (proxiedForMe.FirstOrDefault(v => v.Value == proxiedMessageId).Key, false); // or 0, which means - do not proxy;
        }

        private (int proxiedId, bool sendToMe) GetProxyOfMessageForUser(int targetUser, int originalMessageId)
        {
            if (originalMessageId == 0)
                return (0, false);
            
            if (!_state.UserMessages.TryGetValue(targetUser, out var myMessages))
            {
                _state.UserMessages[targetUser] = myMessages = new List<int>();
            }

            if (myMessages.Contains(originalMessageId))
            {
                return (originalMessageId, true); // was sent by me;
            }

            if (!_state.ForwardedMessageIds.TryGetValue(targetUser, out var proxiedForMe))
            {
                _state.ForwardedMessageIds[targetUser] = proxiedForMe = new Dictionary<int, int>();
            }

            if (proxiedForMe.TryGetValue(originalMessageId, out var proxiedId))
                return (proxiedId, false);

            return (0, false);
        }

        private async void OnMessageEdited(object sender, MessageEventArgs e)
        {
            var otherUsers = _state.EnabledUsers.Where(v => v.Key != e.Message.From.Id).ToList();

            foreach (var pair in otherUsers)
            {
                await SafeExecute(async () =>
                {
                    var userId = pair.Key;
                    var chatId = pair.Value;
                    
                    var originalMessage = GetProxiedMessageOriginalId(userId, e.Message.MessageId);
                    if (originalMessage.sentByMe)
                        return;

                    var proxied = GetProxyOfMessageForUser(userId, originalMessage.originalMessageId);

                    var forwardedMessageId = proxied.proxiedId;

                    if (e.Message.Text != null)
                    {
                        await _botClient.EditMessageTextAsync(chatId, forwardedMessageId, e.Message.Text);
                    }

                    if (e.Message.Caption != null)
                    {
                        await _botClient.EditMessageCaptionAsync(chatId, forwardedMessageId, e.Message.Caption);
                    }

                    if (e.Message.Photo != null)
                    {
                        await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaPhoto(e.Message.Photo.OrderByDescending(v => v.Width).First().FileId));
                    }
                    else if (e.Message.Audio != null)
                    {
                        await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaAudio(e.Message.Audio.FileId));
                    }
                    else if (e.Message.Video != null)
                    {
                        await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaVideo(e.Message.Video.FileId));
                    }
                    else if (e.Message.Document != null)
                    {
                        await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaDocument(e.Message.Document.FileId));
                    }
                    if (e.Message.Location != null)
                    {
                        await _botClient.EditMessageLiveLocationAsync(chatId, forwardedMessageId, e.Message.Location.Latitude, e.Message.Location.Longitude);
                    }
                });
            }
        }

        private async void OnMessage(object sender, MessageEventArgs e)
        {
            try
            {
                if (_state.BannedUsers.Contains(e.Message.From.Id))
                {
                    await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Сорян, вас забанили. До свидания.");
                }
                else if (!_state.EnabledUsers.ContainsKey(e.Message.From.Id))
                {
                    await HandleAuthenticate(e);
                }
                else if (e.Message.Text == "/ban" && IsSentByAdmin(e))
                {
                    await HandleBanCommand(e);
                }
                else
                {
                    await ForwardMessage(e);
                }
            }
            catch (Exception ex)
            {
                await _botClient.SendTextMessageAsync("diverofdark", "СоваБот: Случилась непоправимая ошибка, отправь это @diverofdark плиз, никто не видит это кроме тебя: \n" + ex);
            }
        }

        private async Task HandleAuthenticate(MessageEventArgs e)
        {
            if (e.Message.ForwardFromChat != null && e.Message.ForwardFromChat.Id == -1001429386280)
            {
                _state.EnabledUsers.Add(e.Message.From.Id, e.Message.Chat.Id);
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id,
                    "Чат прямо здесь, просто пиши! Важные нюансы: при создании опроса - видно кто его сделал, удалять сообщения нельзя, зато можно редактировать. В остальном вроде всё работает. Ну и истории нету.");
            }
            else
            {
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Перешли мне любое сообщение с текстом из секретных движухи чтобы вступить в ряды анонимусов FZ!");
            }
        }

        private async Task HandleBanCommand(MessageEventArgs e)
        {
            if (e.Message.ReplyToMessage == null)
            {
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Реплайни на сообщение юзера, которого хочешь забанить");
            }
            else
            {
                var originalMessage = GetProxiedMessageOriginalId(e.Message.From.Id, e.Message.MessageId);
                
                if (originalMessage.originalMessageId != 0 && !originalMessage.sentByMe)
                {
                    var user = _state.UserMessages.FirstOrDefault(v => v.Value.Contains(originalMessage.originalMessageId)).Key;
                    if (user != 0)
                    {
                        _state.BannedUsers.Add(user);
                        foreach (var pair in _state.EnabledUsers)
                        {
                            await SafeExecute(async () =>
                            {
                                var userId = pair.Key;
                                var chatId = pair.Value;

                                var proxied = GetProxyOfMessageForUser(userId, originalMessage.originalMessageId);

                                if (proxied.proxiedId != 0)
                                {
                                    await _botClient.SendTextMessageAsync(chatId, "СоваБот: Забанен автор сообщения", replyToMessageId: proxied.proxiedId);
                                }
                            });
                        }

                        return;
                    }
                }

                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Не могу найти автора сообщения");
            }
        }

        private async Task ForwardMessage(MessageEventArgs e)
        {
            var otherUsers = _state.EnabledUsers.Where(v => v.Key != e.Message.From.Id).ToList();

            if (!_state.UserMessages.TryGetValue(e.Message.From.Id, out var userMessages))
            {
                userMessages = new List<int>();
                _state.UserMessages[e.Message.From.Id] = userMessages;
            }

            userMessages.Add(e.Message.MessageId);

            foreach (var pair in otherUsers)
            {
                await SafeExecute(async () =>
                {
                    var userId = pair.Key;
                    var chatId = pair.Value;

                    int replyToMessageId = 0;

                    var disableNotification = true;

                    if (!_state.ForwardedMessageIds.TryGetValue(userId, out var forwardeds))
                    {
                        forwardeds = new Dictionary<int, int>();
                        _state.ForwardedMessageIds[userId] = forwardeds;
                    }

                    if (e.Message.ReplyToMessage != null)
                    {
                        replyToMessageId = e.Message.ReplyToMessage.MessageId;

                        var original = GetProxiedMessageOriginalId(e.Message.From.Id, replyToMessageId);
                        var proxiedForCurrentUser = GetProxyOfMessageForUser(userId, original.originalMessageId);

                        replyToMessageId = proxiedForCurrentUser.proxiedId;
                        if (original.sentByMe)
                        {
                            disableNotification = false;
                        }
                    }

                    Message msg;

                    switch (e.Message.Type)
                    {
                        case MessageType.Text:
                            msg = await _botClient.SendTextMessageAsync(chatId, e.Message.Text, disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Photo:
                            msg = await _botClient.SendPhotoAsync(chatId, new InputOnlineFile(e.Message.Photo.OrderByDescending(v => v.Height).First().FileId), e.Message.Caption,
                                disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Audio:
                            msg = await _botClient.SendAudioAsync(chatId, new InputOnlineFile(e.Message.Audio.FileId), e.Message.Caption, duration: e.Message.Audio.Duration,
                                performer: e.Message.Audio.Performer,
                                disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Video:
                            msg = await _botClient.SendVideoAsync(chatId, new InputOnlineFile(e.Message.Video.FileId), e.Message.Video.Duration, e.Message.Video.Width,
                                e.Message.Video.Height,
                                disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Voice:
                            msg = await _botClient.SendVoiceAsync(chatId, new InputOnlineFile(e.Message.Voice.FileId), caption: e.Message.Caption, disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Document:
                            msg = await _botClient.SendDocumentAsync(chatId, new InputOnlineFile(e.Message.Document.FileId), caption: e.Message.Caption,
                                disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Sticker:
                            msg = await _botClient.SendStickerAsync(chatId, new InputOnlineFile(e.Message.Sticker.FileId), disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Location:
                            msg = await _botClient.SendLocationAsync(chatId, e.Message.Location.Latitude, e.Message.Location.Longitude, disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Poll:
                            msg = await _botClient.ForwardMessageAsync(chatId, e.Message.Chat.Id, e.Message.MessageId, true);
                            break;
                        case MessageType.Dice:
                            msg = await _botClient.ForwardMessageAsync(chatId, e.Message.Chat.Id, e.Message.MessageId, true);
                            break;
                        case MessageType.MessagePinned:
                            var originalMessage = e.Message.PinnedMessage.MessageId;
                            if (forwardeds.ContainsKey(originalMessage))
                            {
                                originalMessage = forwardeds[originalMessage];
                            }

                            await _botClient.PinChatMessageAsync(chatId, originalMessage);
                            return;
                        case MessageType.Venue:
                        case MessageType.VideoNote:
                        case MessageType.Contact:
                        case MessageType.Game:
                        case MessageType.Invoice:
                        case MessageType.SuccessfulPayment:
                        case MessageType.ChatMembersAdded:
                        case MessageType.ChatMemberLeft:
                        case MessageType.ChatTitleChanged:
                        case MessageType.ChatPhotoChanged:
                        case MessageType.WebsiteConnected:
                        case MessageType.Unknown:
                        case MessageType.ChatPhotoDeleted:
                        case MessageType.GroupCreated:
                        case MessageType.SupergroupCreated:
                        case MessageType.ChannelCreated:
                        case MessageType.MigratedToSupergroup:
                        case MessageType.MigratedFromGroup:
                        default:
                            await _botClient.SendTextMessageAsync("@diverofdark", "СоваБот: Извини, я пока не умею такие типы сообщений посылать. Напиши об этом в личку @diverofdark, он наверное починит", replyToMessageId: e.Message.MessageId);
                            return;
                    }

                    forwardeds[e.Message.MessageId] = msg.MessageId;
                });
            }
        }

        public void Dispose()
        {
            _botClient.StopReceiving();
        }
    }
}