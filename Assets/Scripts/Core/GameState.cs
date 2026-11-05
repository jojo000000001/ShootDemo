namespace ShootDemo.Core
{
    /// <summary>
    /// Values are serialized on the scene state machine, so existing states keep their numbers.
    /// </summary>
    public enum GameState
    {
        Playing = 0,
        Paused = 1,
        GameOver = 2,
        Menu = 3
    }
}
