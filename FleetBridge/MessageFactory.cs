using QuickFix;
using QuickFix.Fields;

namespace FleetBridge;

/// <summary>
/// Message factory of the FIX44-FLEET dialect: every message type is a
/// generic <see cref="Message"/> and every repeating group is a generic
/// <see cref="Group"/> built from the data dictionary.
///
/// This exists because QuickFIX/n's <see cref="DefaultMessageFactory"/>
/// <b>throws</b> <see cref="UnsupportedVersion"/> from its group overload
/// when no generated FIX.4.4 message assembly is loaded — and
/// <c>Message.SetGroup</c> only falls back to a dictionary-driven generic
/// group when the factory returns <b>null</b>. With the default factory and
/// only QuickFIXn.Core referenced, any dialect message carrying a repeating
/// group fails to parse; flat messages (Logon/Welcome, heartbeats) work,
/// which makes the failure easy to miss: the session logs on, then dies on
/// the first grouped command (e.g. U7 SubscribeSymbols) and reconnects
/// forever. Mirror of the engine-side Bot.Fleet.Fix.FleetMessageFactory.
/// </summary>
internal sealed class FleetMessageFactory : IMessageFactory
{
    public ICollection<string> GetSupportedBeginStrings() => ["FIX.4.4"];

    public Message Create(string beginString, string msgType) =>
        Create(beginString, new ApplVerID(QuickFix.FixValues.ApplVerID.FIX44), msgType);

    public Message Create(string beginString, ApplVerID applVerId, string msgType)
    {
        var message = new Message();
        message.Header.SetField(new StringField(Tags.MsgType, msgType));
        return message;
    }

    /// <summary>Always null: groups are parsed generically from the data
    /// dictionary's group spec (counter + delimiter), never from generated
    /// types.</summary>
    public Group? Create(string beginString, string msgType, int groupCounterTag) => null;
}
