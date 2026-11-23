namespace Untrodden
{
    /// <summary>
    /// 스테이지 목록. 스테이지 번호 = 빌드 설정의 씬 순서.
    /// 한 맵에 모두 넣으면 무거우므로 스테이지마다 씬을 따로 만든다(씬 빌더가 생성).
    /// </summary>
    public static class Stages
    {
        public static readonly string[] Titles =
        {
            "1. 튜토리얼",
            "2. 장애물",
            "3. 활공",
            "4. 숲",
        };

        public static readonly string[] Descriptions =
        {
            "폴로 밀어 출발하고, 양손을 돌려 방향을 바꾸고, 꺾어서 속도를 줄이는 법을 익혀요.",
            "바위에 부딪히면 멈추고, 자갈 위를 지나가면 크게 덜컹이며 느려져요. 피해서 내려가세요.",
            "점프대를 타고 날아올라 자갈과 바위를 뛰어넘으세요. 착지할 때는 폴을 들고 진행 방향을 바라봐야 깔끔하게 내려요.",
            "코스 위에도 나무가 빽빽해요. 손을 조금씩 돌려 나무 사이를 섬세하게 빠져나가세요.",
        };

        public static int Count => Titles.Length;
    }
}
