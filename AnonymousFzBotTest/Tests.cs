using AnonymousFzBot;
using NUnit.Framework;

namespace AnonymousFzBotTest
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
            
        }

        [Test]
        public void Test1()
        {
            var state = new State(new State.SerializedState());

            const int userA = 1;
            const int userB = 2;
            const int userC = 3;
            
            // Given all users set up in state
            state.Enable(userA, userA);
            state.Enable(userB, userB);
            state.Enable(userC, userC);
            
            // Given userA sends message 1 to bot
            state.RecordUserSentMessage(userA, 1);
            state.RecordMessageWasForwarded(userB, 1, 2);
            state.RecordMessageWasForwarded(userC, 1,3);

            // Given userA sends reply to message 1 to bot
            state.RecordUserSentMessage(userA, 4);
            state.RecordMessageWasForwarded(userB, 4,5);
            state.RecordMessageWasForwarded(userC, 4,6);

            Assert.AreEqual(state.GetProxiedMessageOriginalId(userA, 1).originalMessageId, 1);
            Assert.AreEqual(state.GetProxiedMessageOriginalId(userB, 2).originalMessageId, 1);
            Assert.AreEqual(state.GetProxiedMessageOriginalId(userC, 3).originalMessageId, 1);
            Assert.AreEqual(state.GetProxyOfMessageForUser(userA, 1).proxiedId, 1);
            Assert.AreEqual(state.GetProxyOfMessageForUser(userB, 1).proxiedId, 2);
            Assert.AreEqual(state.GetProxyOfMessageForUser(userC, 1).proxiedId, 3);

            /*  A | B | C | Bot
             *  A -> Bot (1)
             *  Bot -> B (2)
             *  Bot -> C(3)
             * 
             *  A -> Bot (4 + Reply(1))
             *  Bot -> B(5 + Reply(2))
             *  Bot -> C(6 + Reply(3))
             *
             *  B -> Bot (7 + Reply (2))
             *  Bot -> A (8 + Reply(1))
             *  Bot -> C (9 + Reply(3))
             */

        }
    }
}