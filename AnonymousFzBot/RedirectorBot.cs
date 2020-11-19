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
                Console.Error.WriteLine(ex);
                await _botClient.SendTextMessageAsync(912327, "Exception: " + ex);
            }
        }

        private async void OnMessageEdited(object sender, MessageEventArgs e)
        {
            var otherUsers = _state.GetUsers().Where(v => v.user != e.Message.From.Id).ToList();

            foreach (var pair in otherUsers)
            {
                await SafeExecute(async () =>
                {
                    var originalMessage = _state.GetProxiedMessageOriginalId(pair.user, e.Message.MessageId);
                    if (originalMessage.sentByMe)
                        return;

                    var proxied = _state.GetProxyOfMessageForUser(pair.user, originalMessage.originalMessageId);

                    var forwardedMessageId = proxied.proxiedId;

                    if (e.Message.Text != null)
                    {
                        await _botClient.EditMessageTextAsync(pair.chat, forwardedMessageId, e.Message.Text);
                    }

                    if (e.Message.Caption != null)
                    {
                        await _botClient.EditMessageCaptionAsync(pair.chat, forwardedMessageId, e.Message.Caption);
                    }

                    if (e.Message.Photo != null)
                    {
                        await _botClient.EditMessageMediaAsync(pair.chat, forwardedMessageId, new InputMediaPhoto(e.Message.Photo.OrderByDescending(v => v.Width).First().FileId));
                    }
                    else if (e.Message.Audio != null)
                    {
                        await _botClient.EditMessageMediaAsync(pair.chat, forwardedMessageId, new InputMediaAudio(e.Message.Audio.FileId));
                    }
                    else if (e.Message.Video != null)
                    {
                        await _botClient.EditMessageMediaAsync(pair.chat, forwardedMessageId, new InputMediaVideo(e.Message.Video.FileId));
                    }
                    else if (e.Message.Document != null)
                    {
                        await _botClient.EditMessageMediaAsync(pair.chat, forwardedMessageId, new InputMediaDocument(e.Message.Document.FileId));
                    }
                    if (e.Message.Location != null)
                    {
                        await _botClient.EditMessageLiveLocationAsync(pair.chat, forwardedMessageId, e.Message.Location.Latitude, e.Message.Location.Longitude);
                    }
                });
            }
        }

        private async void OnMessage(object sender, MessageEventArgs e)
        {
            await SafeExecute(async () =>
            {
                if (_state.IsBanned(e.Message.From.Id))
                {
                    await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Сорян, вас забанили. До свидания.");
                }
                else if (!_state.IsEnabled(e.Message.From.Id))
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
            });
        }

        private async Task HandleAuthenticate(MessageEventArgs e)
        {
            if (e.Message.ForwardFromChat != null && e.Message.ForwardFromChat.Id == -1001429386280)
            {
                _state.Enable(e.Message.From.Id, e.Message.Chat.Id);
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
                var originalMessage = _state.GetProxiedMessageOriginalId(e.Message.From.Id, e.Message.MessageId);
                
                if (originalMessage.originalMessageId != 0 && !originalMessage.sentByMe)
                {
                    var user = _state.GetUserIdByMessageId(originalMessage.originalMessageId);
                    if (user != 0)
                    {
                        _state.Ban(user);
                        foreach (var pair in _state.GetUsers())
                        {
                            await SafeExecute(async () =>
                            {
                                var proxied = _state.GetProxyOfMessageForUser(pair.user, originalMessage.originalMessageId);

                                if (proxied.proxiedId != 0)
                                {
                                    await _botClient.SendTextMessageAsync(pair.chat, "СоваБот: Забанен автор сообщения", replyToMessageId: proxied.proxiedId);
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
            var otherUsers = _state.GetUsers().Where(v => v.user != e.Message.From.Id).ToList();

            _state.RecordUserSentMessage(e.Message.From.Id, e.Message.MessageId);

            foreach (var pair in otherUsers)
            {
                await SafeExecute(async () =>
                {
                    int replyToMessageId = 0;

                    var disableNotification = true;

                    if (e.Message.ReplyToMessage != null)
                    {
                        replyToMessageId = e.Message.ReplyToMessage.MessageId;

                        var original = _state.GetProxiedMessageOriginalId(e.Message.From.Id, replyToMessageId);
                        var proxiedForCurrentUser = _state.GetProxyOfMessageForUser(pair.user, original.originalMessageId);

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
                            msg = await _botClient.SendTextMessageAsync(pair.chat, e.Message.Text, disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Photo:
                            msg = await _botClient.SendPhotoAsync(pair.chat, new InputOnlineFile(e.Message.Photo.OrderByDescending(v => v.Height).First().FileId), e.Message.Caption,
                                disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Audio:
                            msg = await _botClient.SendAudioAsync(pair.chat, new InputOnlineFile(e.Message.Audio.FileId), e.Message.Caption, duration: e.Message.Audio.Duration,
                                performer: e.Message.Audio.Performer,
                                disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Video:
                            msg = await _botClient.SendVideoAsync(pair.chat, new InputOnlineFile(e.Message.Video.FileId), e.Message.Video.Duration, e.Message.Video.Width,
                                e.Message.Video.Height,
                                disableNotification: disableNotification, replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Voice:
                            msg = await _botClient.SendVoiceAsync(pair.chat, new InputOnlineFile(e.Message.Voice.FileId), caption: e.Message.Caption, disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Document:
                            msg = await _botClient.SendDocumentAsync(pair.chat, new InputOnlineFile(e.Message.Document.FileId), caption: e.Message.Caption,
                                disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Sticker:
                            msg = await _botClient.SendStickerAsync(pair.chat, new InputOnlineFile(e.Message.Sticker.FileId), disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Location:
                            msg = await _botClient.SendLocationAsync(pair.chat, e.Message.Location.Latitude, e.Message.Location.Longitude, disableNotification: disableNotification,
                                replyToMessageId: replyToMessageId);
                            break;
                        case MessageType.Poll:
                            msg = await _botClient.ForwardMessageAsync(pair.chat, e.Message.Chat.Id, e.Message.MessageId, true);
                            break;
                        case MessageType.Dice:
                            msg = await _botClient.ForwardMessageAsync(pair.chat, e.Message.Chat.Id, e.Message.MessageId, true);
                            break;
                        case MessageType.MessagePinned:
                            var sentMessage = e.Message.PinnedMessage.MessageId;
                            var originalMessage = _state.GetProxiedMessageOriginalId(e.Message.From.Id, sentMessage);
                            var proxiedMessage = _state.GetProxyOfMessageForUser(pair.user, originalMessage.originalMessageId);
                            
                            if (proxiedMessage.proxiedId != 0)
                            {
                                await _botClient.PinChatMessageAsync(pair.chat, proxiedMessage.proxiedId);
                            }
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
                            await _botClient.SendTextMessageAsync(pair.user, "СоваБот: Извини, я пока не умею такие типы сообщений посылать. Напиши об этом в личку @diverofdark, он наверное починит", replyToMessageId: e.Message.MessageId);
                            return;
                    }

                    _state.RecordMessageWasForwarded(pair.user, e.Message.MessageId, msg.MessageId);
                });
            }
        }

        public void Dispose()
        {
            _botClient.StopReceiving();
        }
    }
}