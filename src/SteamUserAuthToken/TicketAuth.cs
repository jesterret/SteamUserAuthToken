using SteamKit2;
using SteamKit2.Internal;

namespace SteamUserAuthToken
{
    internal class TicketAuth : ClientMsgHandler
    {
        public override void HandleMsg(IPacketMsg packetMsg)
        {
            if (packetMsg.MsgType == EMsg.ClientAuthListAck)
            {
                var authAck = new ClientMsgProtobuf<CMsgClientAuthListAck>(packetMsg);
                var acknowledged = new TicketAcceptedCallback(authAck.TargetJobID, authAck.Body);
                Client.PostCallback(acknowledged);
            }
            else if (packetMsg.MsgType == EMsg.ClientTicketAuthComplete)
            {
                var complete = new ClientMsgProtobuf<CMsgClientTicketAuthComplete>(packetMsg);
                var inUse = new TicketAuthCompleteCallback(complete.TargetJobID, complete.Body);
                Client.PostCallback(inUse);
            }
        }
    }
}