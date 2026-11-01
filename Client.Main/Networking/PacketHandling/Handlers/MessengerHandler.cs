using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controls.UI;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Networking.Services;
using Client.Main.Scenes;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Messenger packets: friend list, friend requests, online state, letters and chat room invitations.
    /// Mirrors WSclient.cpp ReceiveFriend* / ReceiveLetter* / ReceiveCreateChatRoomResult (l.9355-9800).
    /// </summary>
    public class MessengerHandler : IGamePacketHandler
    {
        private readonly ILogger<MessengerHandler> _logger;
        private readonly CharacterService _characterService;
        private readonly NetworkManager _networkManager;

        public MessengerHandler(ILoggerFactory loggerFactory, CharacterService characterService, NetworkManager networkManager)
        {
            _logger = loggerFactory.CreateLogger<MessengerHandler>();
            _characterService = characterService;
            _networkManager = networkManager;
        }

        private FriendManager Friends => _networkManager.GetFriendManager();

        /// <summary>MessengerInitialization (0xC0): friend list after entering the game; letters follow as 0xC6.</summary>
        [PacketHandler(0xC0, PacketRouter.NoSubCode)]
        public Task HandleMessengerInitializationAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 7)
                    return Task.CompletedTask;

                var init = new MessengerInitialization(packet);
                int count = init.FriendCount;
                while (count > 0 && MessengerInitialization.GetRequiredSize(count) > packet.Length)
                    count--;

                var friends = new List<FriendEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    var friend = init[i];
                    friends.Add(new FriendEntry { Name = friend.Name, ServerId = friend.ServerId });
                }

                Friends.Initialize(friends, init.MaximumLetterCount);
                _logger.LogInformation("Messenger initialized: {Letters}/{MaxLetters} letters, {Friends} friends.",
                    init.LetterCount, init.MaximumLetterCount, friends.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing MessengerInitialization (0xC0).");
            }

            return Task.CompletedTask;
        }

        /// <summary>FriendAdded (0xC1/0x01).</summary>
        [PacketHandler(0xC1, PacketRouter.NoSubCode)]
        public Task HandleFriendAddedAsync(Memory<byte> packet)
        {
            if (packet.Length < FriendAdded.Length || packet.Span[3] != 0x01)
                return Task.CompletedTask;

            var added = new FriendAdded(packet);
            Friends.AddOrUpdateFriend(added.FriendName, added.ServerId);
            ShowSystemMessage($"{added.FriendName} has been added to your friend list.");
            return Task.CompletedTask;
        }

        /// <summary>FriendRequest (0xC2): another player wants to add us; answered with FriendAddResponse.</summary>
        [PacketHandler(0xC2, PacketRouter.NoSubCode)]
        public Task HandleFriendRequestAsync(Memory<byte> packet)
        {
            if (packet.Length < FriendRequest.Length)
                return Task.CompletedTask;

            string requester = new FriendRequest(packet).Requester;
            MuGame.ScheduleOnMainThread(() => RequestDialog.Show(
                $"{requester} wants to add you as a friend.",
                onAccept: () => _ = _characterService.SendFriendAddResponseAsync(true, requester),
                onReject: () => _ = _characterService.SendFriendAddResponseAsync(false, requester)));
            return Task.CompletedTask;
        }

        /// <summary>FriendDeleted (0xC3/0x01).</summary>
        [PacketHandler(0xC3, PacketRouter.NoSubCode)]
        public Task HandleFriendDeletedAsync(Memory<byte> packet)
        {
            if (packet.Length < FriendDeleted.Length || packet.Span[3] != 0x01)
                return Task.CompletedTask;

            Friends.RemoveFriend(new FriendDeleted(packet).FriendName);
            return Task.CompletedTask;
        }

        /// <summary>FriendOnlineStateUpdate (0xC4): a friend came online (server id) or went offline (0xFF).</summary>
        [PacketHandler(0xC4, PacketRouter.NoSubCode)]
        public Task HandleFriendOnlineStateUpdateAsync(Memory<byte> packet)
        {
            if (packet.Length < FriendOnlineStateUpdate.Length)
                return Task.CompletedTask;

            var update = new FriendOnlineStateUpdate(packet);
            Friends.UpdateFriendServer(update.FriendName, update.ServerId);
            return Task.CompletedTask;
        }

        /// <summary>FriendInvitationResult (0xCB): result of the own friend request.</summary>
        [PacketHandler(0xCB, PacketRouter.NoSubCode)]
        public Task HandleFriendInvitationResultAsync(Memory<byte> packet)
        {
            if (packet.Length < FriendInvitationResult.Length)
                return Task.CompletedTask;

            bool success = new FriendInvitationResult(packet).Success;
            ShowSystemMessage(success
                ? "Your friend request has been sent."
                : "The friend request could not be sent. Check the character name.");
            return Task.CompletedTask;
        }

        /// <summary>LetterSendResponse (0xC5).</summary>
        [PacketHandler(0xC5, PacketRouter.NoSubCode)]
        public Task HandleLetterSendResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < LetterSendResponse.Length)
                return Task.CompletedTask;

            var result = new LetterSendResponse(packet).Result;
            MuGame.ScheduleOnMainThread(() =>
            {
                Friends.RaiseLetterSendResult((byte)result);
                if (result != LetterSendResponse.LetterSendRequestResult.Success)
                    RequestDialog.ShowInfo(GetLetterSendError(result));
                else
                    ShowSystemMessage("Your letter has been sent.");
            });
            return Task.CompletedTask;
        }

        /// <summary>AddLetter (0xC6): letter header after entering the game or when a new letter arrives.</summary>
        [PacketHandler(0xC6, PacketRouter.NoSubCode)]
        public Task HandleAddLetterAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < AddLetter.Length)
                    return Task.CompletedTask;

                var letter = new AddLetter(packet);
                bool isNew = letter.State == AddLetter.LetterState.New;
                var entry = new LetterEntry
                {
                    Index = letter.LetterIndex,
                    Sender = letter.SenderName,
                    Timestamp = letter.Timestamp,
                    Subject = letter.Subject,
                    IsRead = letter.State == AddLetter.LetterState.Read,
                };
                MuGame.ScheduleOnMainThread(() =>
                {
                    Friends.AddLetter(entry, isNew);
                    if (isNew)
                        ShowSystemMessage($"You received a letter from {entry.Sender}.");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AddLetter (0xC6).");
            }

            return Task.CompletedTask;
        }

        /// <summary>OpenLetter (0xC7): letter text; OpenMU sends the extended variant to clients from 1.06.3 on.</summary>
        [PacketHandler(0xC7, PacketRouter.NoSubCode)]
        public Task HandleOpenLetterAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < 28)
                    return Task.CompletedTask;

                ushort index;
                string message;
                var span = packet.Span;
                int messageSize = span[6] | (span[7] << 8);
                if (messageSize > 0 && 28 + messageSize <= packet.Length && packet.Length - 28 - messageSize <= 1)
                {
                    var letter = new OpenLetter(packet);
                    index = letter.LetterIndex;
                    message = letter.Message;
                }
                else
                {
                    var letter = new OpenLetterExtended(packet);
                    index = letter.LetterIndex;
                    message = letter.Message;
                }

                MuGame.ScheduleOnMainThread(() => Friends.SetLetterText(index, message ?? string.Empty));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing OpenLetter (0xC7).");
            }

            return Task.CompletedTask;
        }

        /// <summary>RemoveLetter (0xC8).</summary>
        [PacketHandler(0xC8, PacketRouter.NoSubCode)]
        public Task HandleRemoveLetterAsync(Memory<byte> packet)
        {
            if (packet.Length < RemoveLetter.Length)
                return Task.CompletedTask;

            var removed = new RemoveLetter(packet);
            if (removed.RequestSuccessful)
            {
                ushort index = removed.LetterIndex;
                MuGame.ScheduleOnMainThread(() => Friends.RemoveLetter(index));
            }

            return Task.CompletedTask;
        }

        /// <summary>ChatRoomConnectionInfo (0xCA): connect to the chat server and join the room.</summary>
        [PacketHandler(0xCA, PacketRouter.NoSubCode)]
        public Task HandleChatRoomConnectionInfoAsync(Memory<byte> packet)
        {
            try
            {
                if (packet.Length < ChatRoomConnectionInfo.Length)
                    return Task.CompletedTask;

                var info = new ChatRoomConnectionInfo(packet);
                if (!info.Success)
                {
                    ShowSystemMessage($"Could not open a chat with {info.FriendName}. The friend must be online.");
                    return Task.CompletedTask;
                }

                string host = info.ChatServerIp;
                ushort roomId = info.ChatRoomId;
                uint token = info.AuthenticationToken;
                string friendName = info.FriendName;
                MuGame.ScheduleOnMainThread(() =>
                    (MuGame.Instance?.ActiveScene as GameScene)?.OpenChatRoom(host, roomId, token, friendName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing ChatRoomConnectionInfo (0xCA).");
            }

            return Task.CompletedTask;
        }

        private static string GetLetterSendError(LetterSendResponse.LetterSendRequestResult result) => result switch
        {
            LetterSendResponse.LetterSendRequestResult.MailboxFull => "The mailbox of the receiver is full.",
            LetterSendResponse.LetterSendRequestResult.ReceiverNotExists => "There is no character with this name.",
            LetterSendResponse.LetterSendRequestResult.CantSendToYourself => "You can't send a letter to yourself.",
            LetterSendResponse.LetterSendRequestResult.NotEnoughMoney => "You don't have enough Zen to send a letter.",
            _ => "The letter could not be sent. Please try again.",
        };

        private static void ShowSystemMessage(string text)
        {
            MuGame.ScheduleOnMainThread(() =>
                (MuGame.Instance?.ActiveScene as GameScene)?.ChatLog?.AddMessage("System", text, MessageType.Info));
        }
    }
}
