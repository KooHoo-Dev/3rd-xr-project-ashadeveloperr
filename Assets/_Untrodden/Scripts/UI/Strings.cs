namespace Untrodden.UI
{
    /// <summary>
    /// Every piece of on-screen text. Kept in one place so the font builder can pre-render
    /// all the Korean glyphs the game uses (no hitch when a new character first appears).
    /// </summary>
    public static class Strings
    {
        public const string TitleName = "UNTRODDEN";
        public const string TitleBody =
            "<size=40><b>미답지, 발길이 닿지 않은 곳</b></size>\n\n" +
            "얼어붙은 타이가를 가로질러 북쪽 바닷가 마을까지 살아서 도착하세요.\n" +
            "물은 아래로 흐르고, 강은 바다로 갑니다.\n\n" +
            "<size=26>왼손 스틱: 걷기    오른손 스틱: 회전 / 앞으로 밀면 순간이동\n" +
            "그립: 잡기    트리거: 사격    손을 입으로: 먹기·마시기\n" +
            "장작 3개를 한곳에 떨어뜨리고, 그 위에서 파이어스틱을 긁으세요</size>\n\n" +
            "<size=24><color=#8aa>제작 {0}</color></size>";
        public const string Start = "시작";

        public const string DeathTitle = "쓰러졌습니다";
        public const string DeathCold = "추위를 이기지 못했습니다.";
        public const string DeathHunger = "배고픔으로 쓰러졌습니다.";
        public const string DeathThirst = "목마름으로 쓰러졌습니다.";
        public const string DeathBear = "곰이 더 빨랐습니다.";
        public const string ResumeCampfire = "마지막 모닥불에서 다시 시작";
        public const string ResumeStart = "출발점에서 다시 시작";

        public const string WinTitle = "도착했습니다";
        public const string WinBody =
            "걸린 시간   {0:00}:{1:00}\n" +
            "다시 시작한 횟수   {2}\n" +
            "걸은 거리   {3:0.00} km\n\n" +
            "<size=24><color=#8aa>숲 시드 {4}</color></size>";
        public const string NewForest = "새로운 숲";
        public const string SameForest = "같은 숲 다시";

        public const string Cold = "추위";
        public const string Hunger = "배고픔";
        public const string Thirst = "목마름";
        public const string Warm = "따뜻함";
        public const string Ammo = "탄약 {0} + {1}";
        public const string BlizzardNow = "눈보라 - 피할 곳을 찾으세요";
        public const string BlizzardSoon = "눈보라까지 {0}초";

        /// <summary>All text above plus digits and symbols, for pre-populating the font atlas.</summary>
        public static string AllText =>
            TitleName + TitleBody + Start + DeathTitle + DeathCold + DeathHunger + DeathThirst + DeathBear +
            ResumeCampfire + ResumeStart + WinTitle + WinBody + NewForest + SameForest +
            Cold + Hunger + Thirst + Warm + Ammo + BlizzardNow + BlizzardSoon +
            "0123456789:.+-/()·ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ";
    }
}
