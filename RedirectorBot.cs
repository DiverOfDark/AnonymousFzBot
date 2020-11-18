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
                    await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaPhoto(e.Message.Photo.OrderByDescending(v => v.Width).First().FileUniqueId));
                }
                else if (e.Message.Audio != null)
                {
                    await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaAudio(e.Message.Audio.FileUniqueId));
                }
                else if (e.Message.Video != null)
                {
                    await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaVideo(e.Message.Video.FileUniqueId));
                }
                else if (e.Message.Document != null)
                {
                    await _botClient.EditMessageMediaAsync(chatId, forwardedMessageId, new InputMediaDocument(e.Message.Document.FileUniqueId));
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
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Чат прямо здесь, просто пиши!");
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Перешли мне любое сообщение с текстом из секретных движухи чтобы вступить в ряды анонимусов FZ!");
                    }
                }
                else
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

                        if (e.Message.ReplyToMessage != null) {
                            replyToMessageId = e.Message.ReplyToMessage.MessageId;

                            if (!forwardeds.TryGetValue(replyToMessageId, out var proxiedMessage))
                            {
                                // bot haven't forwarded this message to user. I believe he is the author.
                                disableNotification = false;
                                replyToMessageId = 0;
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
                                msg = await _botClient.SendPhotoAsync(chatId, new InputOnlineFile(e.Message.Photo.OrderByDescending(v => v.Height).First().FileUniqueId), e.Message.Caption,
                                    disableNotification: disableNotification, 
                                    replyToMessageId: replyToMessageId);
                                break;
                            case MessageType.Audio:
                                msg = await _botClient.SendAudioAsync(chatId, new InputOnlineFile(e.Message.Audio.FileUniqueId), e.Message.Caption, duration: e.Message.Audio.Duration,
                                    performer: e.Message.Audio.Performer,

                                    disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                                break;
                            case MessageType.Video:
                                msg = await _botClient.SendVideoAsync(chatId, new InputOnlineFile(e.Message.Video.FileUniqueId), e.Message.Video.Duration, e.Message.Video.Width,
                                    e.Message.Video.Height,
                                    disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                                break;
                            case MessageType.Voice:
                                msg = await _botClient.SendVoiceAsync(chatId, new InputOnlineFile(e.Message.Voice.FileUniqueId), caption: e.Message.Caption, disableNotification: disableNotification,
                                    replyToMessageId: replyToMessageId);
                                break;
                            case MessageType.Document:
                                msg = await _botClient.SendDocumentAsync(chatId, new InputOnlineFile(e.Message.Document.FileUniqueId), caption: e.Message.Caption,
                                    disableNotification: disableNotification,
                                    replyToMessageId: replyToMessageId);
                                break;
                            case MessageType.Sticker:
                                msg = await _botClient.SendStickerAsync(chatId, new InputOnlineFile(e.Message.Sticker.FileUniqueId), disableNotification: disableNotification,
                                    replyToMessageId: replyToMessageId);
                                break;
                            case MessageType.Location:
                                msg = await _botClient.SendLocationAsync(chatId, e.Message.Location.Latitude, e.Message.Location.Longitude, disableNotification: disableNotification,
                                    replyToMessageId: replyToMessageId);
                                break;
                            case MessageType.Poll:
                                msg = await _botClient.ForwardMessageAsync(e.Message.Chat.Id, chatId, e.Message.MessageId, true);
                                break;
                            case MessageType.Dice:
                                await _botClient.DeleteMessageAsync(e.Message.Chat.Id, e.Message.MessageId);
                                var newDice = await _botClient.SendDiceAsync(e.Message.Chat.Id, disableNotification: true, replyToMessageId: e.Message.ReplyToMessage?.MessageId ?? 0);
                                msg = await _botClient.ForwardMessageAsync(e.Message.Chat.Id, chatId, newDice.MessageId, true);
                                break;
                            case MessageType.Venue:
                            case MessageType.VideoNote:
                            case MessageType.Contact:
                            case MessageType.Game:
                            case MessageType.Invoice:
                            case MessageType.SuccessfulPayment:
                            case MessageType.MessagePinned:
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
                                    "Извини, я пока не умею такие типы сообщений посылать. Напиши об этом в личку @diverofdark, он наверное починит", replyToMessageId: e.Message.MessageId);
                                return;
                        }

                        forwardeds[e.Message.MessageId] = msg.MessageId;
                    }
                }
            }
            catch (Exception ex)
            {
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Случилась непоправимая ошибка, отправь это @diverofdark плиз: \n" + ex);
            }
        }

        public void Dispose()
        {
            _botClient.StopReceiving();
        }
    }
}