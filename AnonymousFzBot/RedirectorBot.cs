using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

namespace AnonymousFzBot
{
    internal class RedirectorBot : IDisposable
    {
        private readonly TelegramBotClient _botClient;
        private readonly State _state;
        private static List<string> _adminList;

        public RedirectorBot(TelegramBotClient botClient, State state, List<string> admins)
        {
            _botClient = botClient;
            _state = state;
            _adminList = admins;
            _botClient.StartReceiving();
            _botClient.OnMessage += OnMessage;
            _botClient.OnMessageEdited += OnMessageEdited;
            _botClient.OnCallbackQuery += OnCallbackQuery;
        }

        private async void OnCallbackQuery(object? sender, CallbackQueryEventArgs e)
        {
            await _botClient.AnswerCallbackQueryAsync(e.CallbackQuery.Id);
        }

        private static bool IsSentByAdmin(MessageEventArgs e) => _adminList.Contains(e.Message.From.Username);

        private async Task SafeExecute(Func<Task> action, Func<Exception, Task<bool>> onError = null)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                bool ignore = false;
                if (onError != null)
                {
                    ignore = await onError(ex);
                }
                
                if (!ignore)
                {
                    Console.Error.WriteLine(ex);
                    await _botClient.SendTextMessageAsync(912327, "Exception: " + ex);
                }
            }
        }

        private async void OnMessageEdited(object sender, MessageEventArgs e)
        {
            var otherUsers = _state.GetUsers().Where(v => v.user != e.Message.From.Id).ToList();

            foreach (var pair in otherUsers)
            {
                await SafeExecute(async () =>
                {
                    var originalMessage = _state.GetProxiedMessageOriginalId(e.Message.From.Id, e.Message.MessageId);
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
            _state.StoreUserName(e.Message.From.Id, e.Message.From.Username);
            
            await SafeExecute(async () =>
            {
                if (_state.IsBanned(e.Message.From.Id))
                {
                    await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Сорян, вас забанили. До свидания.", replyToMessageId:e.Message.MessageId, replyMarkup: SelfSign());
                }
                else if (!_state.IsEnabled(e.Message.From.Id))
                {
                    await HandleAuthenticate(e);
                }
                else if (e.Message.Text == "/ban" && IsSentByAdmin(e))
                {
                    await HandleBanCommand(e);
                }
                else if (e.Message.Text == "/unban" && IsSentByAdmin(e))
                {
                    await HandleUnbanCommand(e);
                }
                else if (e.Message.Text == "/users")
                {
                    var lastOnline = _state.GetUserNames();
                    var users = lastOnline.ToList();
                    if (users.Count < 5)
                    {
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id,
                            "СоваБот: Слишком мало пользователей отметилось с момента ввода этого функционала. Как только будет хотя бы 5 - начну выводить последних людей кто был онлайн (по алфавиту)",replyMarkup: SelfSign());
                    }
                    else
                    {
                        users.Sort(); // we don't want to lose anonymity because of ordering by last message
                        var inlineMsg = string.Join("\n", users.Select(v => "@" + v));

                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, $"СоваБот: Всего с ботом общалось {users.Count} человек:\n{inlineMsg}",replyMarkup: SelfSign());
                    }
                }
                else if (e.Message.Text != null && e.Message.Text.StartsWith("/sign"))
                {
                    e.Message.Text = e.Message.Text.Substring("/sign".Length);
                    if (string.IsNullOrWhiteSpace(e.Message.Text))
                    {
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Не тыкай по ссылке, напиши руками :)",replyMarkup: SelfSign());
                    }
                    else
                    {
                        await ForwardMessage(e, true);
                    }
                }
                else if (e.Message.Text == "/help")
                {
                    await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Справка по боту:\n" +
                                                                             "/help - выводит этот текст\n" +
                                                                             "/ban и /unban - позволяет забанить человека (админу)\n" +
                                                                             "/users - показывает список пользователей\n" +
                                                                             "/sign <текст> - позволяет отправить текст с подписью.",replyMarkup: SelfSign());
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
                    "СоваБот: Чат прямо здесь, просто пиши! Важные нюансы: при создании опроса - видно кто его сделал, удалять сообщения нельзя, зато можно редактировать. В остальном вроде всё работает. Ну и истории нету.",replyMarkup: SelfSign());
                
                foreach (var subpair in _state.GetUsers())
                {
                    if (subpair.chat != e.Message.Chat.Id)
                    {
                        await SafeExecute(async () => { await _botClient.SendTextMessageAsync(subpair.chat, "СоваБот: Кто-то пришёл в чат",replyMarkup: SelfSign(), disableNotification: true); });
                    }
                }
            }
            else
            {
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "Перешли мне любое сообщение с текстом из Секретных Движух чтобы вступить в ряды анонимусов FZ! Это нужно, чтобы убедиться что ты из ФЗ, а не какой-то левый человек.");
            }
        }

        private Task HandleUnbanCommand(MessageEventArgs e) => HandleBanCommand(e, true);

        private async Task HandleBanCommand(MessageEventArgs e, bool unban = false)
        {
            if (e.Message.ReplyToMessage == null)
            {
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Реплайни на сообщение юзера, которого хочешь забанить",replyMarkup: SelfSign());
                return;
            }

            var originalMessage = _state.GetProxiedMessageOriginalId(e.Message.From.Id, e.Message.ReplyToMessage.MessageId);

            if (originalMessage.originalMessageId == 0 || originalMessage.sentByMe)
            {
                Console.WriteLine(JsonConvert.SerializeObject(originalMessage));
                await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Не могу найти автора сообщения",replyMarkup: SelfSign());
                return;
            }

            var user = _state.GetUserIdByMessageId(originalMessage.originalMessageId);
            if (user != 0)
            {
                if (unban)
                {
                    _state.Unban(user);
                }
                else
                {
                    if (user == 912327) // @diverofdark TBD: add Masko Id
                    {
                        await _botClient.SendTextMessageAsync(e.Message.Chat.Id, "СоваБот: Админа нельзя забанить",replyMarkup: SelfSign());
                        return;
                    }

                    _state.Ban(user);
                }

                foreach (var pair in _state.GetUsers())
                {
                    await SafeExecute(async () =>
                    {
                        var proxied = _state.GetProxyOfMessageForUser(pair.user, originalMessage.originalMessageId);

                        if (proxied.proxiedId != 0)
                        {
                            await _botClient.SendTextMessageAsync(pair.chat, unban ? "СоваБот: Разбанен автор сообщения" : "СоваБот: Забанен автор сообщения",
                                replyToMessageId: proxied.proxiedId,
                                replyMarkup: SelfSign());
                        }
                    });
                }
            }
        }

        private async Task ForwardMessage(MessageEventArgs e, bool sign = false)
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
                        var original = _state.GetProxiedMessageOriginalId(e.Message.From.Id, e.Message.ReplyToMessage.MessageId);

                        var proxiedForCurrentUser = _state.GetProxyOfMessageForUser(pair.user, original.originalMessageId);

                        // User A send 2 to bot, which was a reply to 1
                        // original message id was O(1)
                        // message to send reply to R(O(1))
                        // disable notification should be if R(O(1)) == O(1)
                        
                        disableNotification = !proxiedForCurrentUser.sendToMe;
                        replyToMessageId = proxiedForCurrentUser.proxiedId;
                    }

                    Message msg;

                    InlineKeyboardMarkup ikb = null;

                    if (sign)
                    {
                        ikb = new InlineKeyboardMarkup(new InlineKeyboardButton
                        {
                            Text = $"{e.Message.From.Username} ({(e.Message.From.FirstName + " " + e.Message.From.LastName).Trim()})",
                            Url = "tg://user?id=" + e.Message.From.Id,
                        });
                    }

                    switch (e.Message.Type)
                    {
                        case MessageType.Text:
                            msg = await _botClient.SendTextMessageAsync(pair.chat, e.Message.Text, disableNotification: disableNotification, replyToMessageId: replyToMessageId, replyMarkup: ikb);
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
                            msg = await _botClient.ForwardMessageAsync(pair.chat, e.Message.Chat.Id, e.Message.MessageId, disableNotification);
                            break;
                        case MessageType.Dice:
                            msg = await _botClient.ForwardMessageAsync(pair.chat, e.Message.Chat.Id, e.Message.MessageId, disableNotification);
                            break;
                        case MessageType.MessagePinned:
                            var sentMessage = e.Message.PinnedMessage.MessageId;
                            var originalMessage = _state.GetProxiedMessageOriginalId(e.Message.From.Id, sentMessage);
                            var proxiedMessage = _state.GetProxyOfMessageForUser(pair.user, originalMessage.originalMessageId);

                            if (proxiedMessage.proxiedId != 0)
                            {
                                await _botClient.PinChatMessageAsync(pair.chat, proxiedMessage.proxiedId, disableNotification);
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
                            await _botClient.SendTextMessageAsync(pair.user, "СоваБот: Извини, я пока не умею такие типы сообщений посылать. Напиши об этом в личку @diverofdark, он наверное починит",
                                replyToMessageId: e.Message.MessageId,
                                replyMarkup: SelfSign());
                            return;
                    }

                    _state.RecordMessageWasForwarded(pair.user, e.Message.MessageId, msg.MessageId);
                }, async ex =>
                {
                    if (ex.Message.Contains("Forbidden: bot was blocked by the user") || ex.Message.Contains("Forbidden: user is deactivated"))
                    {
                        _state.Disable(pair.user);

                        foreach (var subpair in _state.GetUsers())
                        {
                            await SafeExecute(async () => { await _botClient.SendTextMessageAsync(subpair.chat, "СоваБот: Кто-то покинул чат", replyMarkup: SelfSign()); });
                        }

                        return true;
                    }

                    return false;
                });
            }
        }

        private InlineKeyboardMarkup SelfSign() => new InlineKeyboardMarkup(new InlineKeyboardButton
        {
            Text = $"СоваБот",
            Url = "tg://user?id=0"
        });

        public void Dispose()
        {
            _botClient.StopReceiving();
        }
    }
}
