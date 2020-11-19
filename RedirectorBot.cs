using System;
using System.Collections.Generic;
using System.Linq;
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

        private async void OnMessageEdited(object sender, MessageEventArgs e)
        {
            var otherUsers = _state.EnabledUsers.Where(v => v.Key != e.Message.From.Id).ToList();

            foreach (var pair in otherUsers)
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

                if (!forwardeds.TryGetValue(e.Message.MessageId, out var forwardedMessageId))
                {
                    // this is a chat with message sender;
                    continue;
                }

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
                    if (e.Message.ForwardFromChat != null && e.Message.ForwardFromChat.Id == -1001429386280)
                    {
                        _state.EnabledUsers.Add(e.Message.From.Id, e.Message.Chat.Id);
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Чат прямо здесь, просто пиши! Важные нюансы: при создании опроса - видно кто его сделал, удалять сообщения нельзя, зато можно редактировать. В остальном вроде всё работает. Ну и истории нету.");
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Перешли мне любое сообщение с текстом из секретных движухи чтобы вступить в ряды анонимусов FZ!");
                    }
                }
                else if (e.Message.Text == "/ban" && e.Message.From.Username == "diverofdark")
                {
                    if (e.Message.ReplyToMessage == null)
                    {
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Реплайни на сообщение юзера, которого хочешь забанить");
                    }
                    else
                    {
                        var proxiedMessage = e.Message.ReplyToMessage.MessageId;
                        var originalMessage = _state.ForwardedMessageIds[e.Message.From.Id].FirstOrDefault(v => v.Value == proxiedMessage).Key;
                        if (originalMessage != 0)
                        {
                            var user = _state.UserMessages.FirstOrDefault(v => v.Value.Contains(originalMessage)).Key;
                            if (user != 0)
                            {
                                _state.BannedUsers.Add(user);
                                foreach (var pair in _state.EnabledUsers)
                                {
                                    var userId = pair.Key;
                                    var chatId = pair.Value;

                                    int replyToMessageId = originalMessage;

                                    if (!_state.ForwardedMessageIds.TryGetValue(userId, out var forwardeds))
                                    {
                                        forwardeds = new Dictionary<int, int>();
                                        _state.ForwardedMessageIds[userId] = forwardeds;
                                    }

                                    if (!forwardeds.TryGetValue(replyToMessageId, out var proxiedMessages))
                                    {
                                        // bot haven't forwarded this message to user. I believe he is the author.
                                        if (_state.ForwardedMessageIds.TryGetValue(e.Message.From.Id, out var senderForwards))
                                        {
                                            replyToMessageId = senderForwards.FirstOrDefault(v => v.Value == replyToMessageId).Key;
                                        }
                                        else
                                        {
                                            replyToMessageId = 0;
                                        }
                                    }
                                    else
                                    {
                                        replyToMessageId = proxiedMessages;
                                    }

                                    await _botClient.SendTextMessageAsync(chatId, "СоваБот: Забанен автор сообщения", replyToMessageId: replyToMessageId);
                                }
                                return;
                            }
                        }

                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Не могу найти автора сообщения");
                    }
                }
                else
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
                        var userId = pair.Key;
                        var chatId = pair.Value;

                        int replyToMessageId = 0;

                        var disableNotification = true;

                        if (!_state.ForwardedMessageIds.TryGetValue(userId, out var forwardeds))
                        {
                            forwardeds = new Dictionary<int, int>();
                            _state.ForwardedMessageIds[userId] = forwardeds;
                        }

                        if (e.Message.ReplyToMessage != null) {
                            replyToMessageId = e.Message.ReplyToMessage.MessageId;

                            if (!forwardeds.TryGetValue(replyToMessageId, out var proxiedMessage))
                            {
                                // bot haven't forwarded this message to user. I believe he is the author.
                                disableNotification = false;
                                if (_state.ForwardedMessageIds.TryGetValue(e.Message.From.Id, out var senderForwards))
                                {
                                    replyToMessageId = senderForwards.FirstOrDefault(v => v.Value == replyToMessageId).Key;
                                }
                                else
                                {
                                    replyToMessageId = 0;
                                }
                            }
                            else
                            {
                                replyToMessageId = proxiedMessage;
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
                                continue;
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
                                await _botClient.SendTextMessageAsync(e.Message.Chat.Id,
                                    "СоваБот: Извини, я пока не умею такие типы сообщений посылать. Напиши об этом в личку @diverofdark, он наверное починит", replyToMessageId: e.Message.MessageId);
                                return;
                        }
                        forwardeds[e.Message.MessageId] = msg.MessageId;
                    }
                }
            }
            catch (Exception ex)
            {
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Случилась непоправимая ошибка, отправь это @diverofdark плиз, никто не видит это кроме тебя: \n" + ex);
            }
        }

        public void Dispose()
        {
            _botClient.StopReceiving();
        }
    }
}