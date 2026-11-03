namespace Hisa.App;

/// <summary>
/// Greeting is spoken exactly once per explicitly started hands-free session.
/// Never activates the microphone; microphone permission remains independent.
/// </summary>
public sealed class MayVoiceGreeting
{
    public const string Opening =
        "Dạ, Mây Mây chào anh. Hôm nay anh muốn Mây giúp gì ạ?";

    private int _lastGreetedSession = -1;

    public bool TryBegin(int session, bool active)
    {
        if(!active || session<=0 || session==_lastGreetedSession)
            return false;
        _lastGreetedSession=session;
        return true;
    }
}
