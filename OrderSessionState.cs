namespace FastOrder
{
    public enum OrderSessionState
    {
        Draft,
        Confirmed,
        Waiting,
        PreWarming,
        Ready,
        Running,
        Paused,
        Completed,
        Canceled,
        Failed
    }
}
