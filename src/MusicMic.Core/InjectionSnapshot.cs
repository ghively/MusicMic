namespace MusicMic.Core;

public sealed record InjectionSnapshot
{
    private InjectionSnapshot(
        InjectionState state,
        bool isInjectionActive,
        bool isSourceAvailable,
        bool isMicrophoneAvailable,
        bool isOutputAvailable)
    {
        State = state;
        IsInjectionActive = isInjectionActive;
        IsSourceAvailable = isSourceAvailable;
        IsMicrophoneAvailable = isMicrophoneAvailable;
        IsOutputAvailable = isOutputAvailable;
    }

    public static InjectionSnapshot Ready { get; } = new(
        InjectionState.Ready,
        isInjectionActive: false,
        isSourceAvailable: true,
        isMicrophoneAvailable: true,
        isOutputAvailable: true);

    public InjectionState State { get; private init; }

    public bool IsInjectionActive { get; private init; }

    public bool IsSourceAvailable { get; private init; }

    public bool IsMicrophoneAvailable { get; private init; }

    public bool IsOutputAvailable { get; private init; }

    public bool IsSourceStreamActive =>
        IsInjectionActive && IsOutputAvailable && IsSourceAvailable;

    public bool IsMicrophoneStreamActive =>
        IsInjectionActive && IsOutputAvailable && IsMicrophoneAvailable;

    public InjectionSnapshot Start()
    {
        if (!IsOutputAvailable)
        {
            return this with
            {
                State = InjectionState.OutputUnavailable,
                IsInjectionActive = false,
            };
        }

        return this with
        {
            State = ResolveState(
                isInjectionActive: true,
                IsSourceAvailable,
                IsMicrophoneAvailable,
                IsOutputAvailable),
            IsInjectionActive = true,
        };
    }

    public InjectionSnapshot Stop() => this with
    {
        State = ResolveState(
            isInjectionActive: false,
            IsSourceAvailable,
            IsMicrophoneAvailable,
            IsOutputAvailable),
        IsInjectionActive = false,
    };

    public InjectionSnapshot SetSourceAvailable(bool isAvailable) => this with
    {
        State = ResolveState(
            IsInjectionActive,
            isAvailable,
            IsMicrophoneAvailable,
            IsOutputAvailable),
        IsSourceAvailable = isAvailable,
    };

    public InjectionSnapshot SetMicrophoneAvailable(bool isAvailable) => this with
    {
        State = ResolveState(
            IsInjectionActive,
            IsSourceAvailable,
            isAvailable,
            IsOutputAvailable),
        IsMicrophoneAvailable = isAvailable,
    };

    public InjectionSnapshot SetOutputAvailable(bool isAvailable) => this with
    {
        State = ResolveState(
            isAvailable && IsInjectionActive,
            IsSourceAvailable,
            IsMicrophoneAvailable,
            isAvailable),
        IsInjectionActive = isAvailable && IsInjectionActive,
        IsOutputAvailable = isAvailable,
    };

    private static InjectionState ResolveState(
        bool isInjectionActive,
        bool isSourceAvailable,
        bool isMicrophoneAvailable,
        bool isOutputAvailable)
    {
        if (!isOutputAvailable)
        {
            return InjectionState.OutputUnavailable;
        }

        if (!isSourceAvailable)
        {
            return InjectionState.SourceUnavailable;
        }

        if (!isMicrophoneAvailable)
        {
            return InjectionState.MicrophoneUnavailable;
        }

        return isInjectionActive ? InjectionState.Injecting : InjectionState.Ready;
    }
}
