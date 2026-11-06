// Typing belongs to a conversation: a character writing in one chat must not show in another.

using System.Collections.ObjectModel;
using NUnit.Framework;
using Skylarping.Backend;
using Skylarping.Models;

namespace Skylarping.Tests
{
    [TestFixture]
    public class TypingTrackerTests
    {
        private readonly User _bob = new User("Bob", "bob", "bob");
        private readonly User _mina = new User("Mina", "mina", "mina");
        private ObservableCollection<User> _visible;
        private TypingTracker _typing;

        [SetUp]
        public void SetUp()
        {
            _visible = new ObservableCollection<User>();
            _typing = new TypingTracker(_visible);
        }

        [Test]
        public void ShowsOnlyTheOpenConversation()
        {
            _typing.Open("a");
            _typing.Set("a", _bob, true);
            _typing.Set("b", _mina, true);

            CollectionAssert.AreEqual(new[] { _bob }, _visible);
        }

        [Test]
        public void TypingThatStartedElsewhereShowsWhenTheChatOpens()
        {
            _typing.Open("a");
            _typing.Set("b", _mina, true);
            _typing.Open("b");
            CollectionAssert.AreEqual(new[] { _mina }, _visible);

            _typing.Open("a");
            CollectionAssert.IsEmpty(_visible);
        }

        [Test]
        public void ReopeningAChatAfterTheUiClearedTheListShowsTypingAgain()
        {
            _typing.Open("a");
            _typing.Set("a", _bob, true);
            _visible.Clear(); // the window does this on every conversation change
            _typing.Open("a");

            CollectionAssert.AreEqual(new[] { _bob }, _visible);
        }

        [Test]
        public void StoppingElsewhereLeavesTheSamePersonTypingHere()
        {
            _typing.Open("a");
            _typing.Set("a", _bob, true);
            _typing.Set("group", _bob, true);
            _typing.Set("group", _bob, false);

            CollectionAssert.AreEqual(new[] { _bob }, _visible);
            _typing.Set("a", _bob, false);
            CollectionAssert.IsEmpty(_visible);
        }

        [Test]
        public void ClearingAConversationLeavesOthersAlone()
        {
            _typing.Open("a");
            _typing.Set("a", _bob, true);
            _typing.Set("a", _mina, true);
            _typing.Set("b", _mina, true);
            _typing.Clear("a");
            CollectionAssert.IsEmpty(_visible);

            _typing.Open("b");
            CollectionAssert.AreEqual(new[] { _mina }, _visible);
        }
    }
}
