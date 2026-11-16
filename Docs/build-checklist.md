# Untrodden — 빌드와 테스트 체크리스트

필수 구현 범위 전체(시작 → 횡단 → 위기 → 도착)가 들어간 첫 버전이다. 아래 순서대로 열고, 빌드하고, 확인한다.

## 1. 에디터에서 씬 만들기

1. Unity Hub에서 이 프로젝트를 **Unity 6000.3.15f1**로 연다.
2. `File > Build Profiles`에서 **Android**로 전환한다.
3. 콘솔에 **컴파일 에러가 없는지** 확인한다. 에러가 있으면 메시지를 그대로 복사해서 알려주기.
4. 상단 메뉴 **`Untrodden > Build Game Scene`** 을 누른다.
   - `Assets/_Untrodden/Materials`에 머티리얼, `Assets/_Untrodden/GameAssets.asset`이 만들어진다.
   - 한글 폰트는 프로젝트에 원래 있던 `Assets/99. External Assets/Font/Noto_Sans_KR`을 그대로 쓴다. 게임 안 글자는 모두 한글이다.
   - `Assets/_Untrodden/Scenes/Untrodden.unity`가 만들어지고 빌드 첫 번째 씬으로 등록된다.
   - 앱 이름이 `Untrodden`이 된다. 타이틀의 제작자 이름은 `GameAssets.asset`의 **Creator Name**에서 바꾼다.
5. `Edit > Project Settings > XR Plug-in Management > Project Validation`에서 Android 탭 경고를 **Fix All**.

## 2. Quest에 빌드

1. Quest 개발자 모드, USB 연결, USB 디버깅 허용.
2. `File > Build Profiles > Android > Run Device`에서 Quest 선택 후 **Build And Run**.

## 3. 조작

| 입력 | 동작 |
| --- | --- |
| 왼손 스틱 | 걷기 (걷는 동안 화면 가장자리 어두워짐) |
| 오른손 스틱 좌우 | 45° 스냅 회전 |
| 오른손 스틱 앞으로 → 놓기 | 순간이동 |
| 그립 | 잡기 (가까이 또는 레이로) |
| 소총을 쥔 채 트리거 | 사격 |
| 오른쪽 허리 주머니를 그립 | 총알 한 발 꺼내기 |
| 총알을 소총 오른쪽 몸통에 갖다 대기 | 장전 |
| 장작을 땅에 떨어뜨리기 | 모닥불 자리 생성 / 근처 모닥불에 장작 끼우기 |
| 장작 3개 위에서 파이어스틱을 빠르게 긁기 | 불꽃 → 두 번이면 점화 |
| 음식을 입에 가져가기 | 먹기 |
| 빈손을 강물에 담갔다가 입에 | 물 마시기 |
| 빈손으로 눈을 짚었다가 입에 | 눈 먹기 (목마름 조금 ↓, 몸이 차가워짐) |
| 왼쪽 손목 보기 | 체력, 체력을 깎는 원인, 탄약, 눈보라 경고, 시간 |

시작 장비: 오른쪽 어깨 소총(2발 장전), 왼쪽 허리 파이어스틱, 가슴 나침반(빨간 바늘이 북쪽 = 바다), 총알 주머니 3발.

## 4. 헤드셋에서 확인할 것

### 시작
- [ ] 앱이 튕기지 않고 타이틀 화면(UNTRODDEN, 미답지, 제작자 이름)이 보인다
- [ ] 한글이 네모(□)로 깨지지 않는다
- [ ] 레이로 **시작** 버튼을 누를 수 있다

### 숲과 이동
- [ ] 발밑에 지형이 있고 떨어지지 않는다
- [ ] 걸어가면 앞쪽 지형·나무가 안개 속에서 자연스럽게 나타난다 (갑자기 튀어나오는 게 보이면 알려주기)
- [ ] 내리막을 따라가면 개울 → 강으로 이어진다
- [ ] 순간이동 포물선이 지형에 닿는다
- [ ] 나무를 통과하지 못한다

### 생존
- [ ] 손목에 체력 바와 추위 / 배고픔 / 목마름이 보인다
- [ ] 장작 3개를 한곳에 떨어뜨리고 파이어스틱으로 불을 붙일 수 있다
- [ ] 불 옆에 있으면 **따뜻함**이 뜨고 체력이 오른다
- [ ] 강물·눈을 손으로 떠서 먹을 수 있다
- [ ] 강가에서 물고기, 오두막에서 통조림·탄약 상자를 주울 수 있다

### 위기
- [ ] 약 2분 뒤 손목에 "눈보라까지 30초" 경고 → 눈보라 (시야가 크게 줄고 바람 소리가 커짐)
- [ ] 강가에서 곰이 나타난다. 물러서면 떠나고, 다가가면 돌진한다
- [ ] 소총 사격, 장전이 된다. 머리에 맞으면 곰이 쓰러진다
- [ ] 체력이 0이 되면 원인이 표시되고, 마지막 모닥불(없으면 출발점)에서 다시 시작한다

### 도착
- [ ] 강을 따라 북쪽 끝에 마을 불빛(높은 불빛 기둥)이 있다
- [ ] 마을 광장에 들어가면 결과 화면(시간, 다시 시작 횟수, 걸은 거리)이 나온다
- [ ] **새로운 숲**을 누르면 다른 숲이 만들어진다

### 성능
- [ ] 걷는 동안, 순간이동 직후, 눈보라 중에 끊김이 눈에 띄지 않는다

## 조정할 수 있는 값

| 파일 | 값 | 의미 |
| --- | --- | --- |
| `World/WorldMap.cs` | `Width`, `Length`, `CoastZ` | 월드 크기 (지금 800 × 1200m, 해안 1100m) |
| `World/ChunkStreamer.cs` | `m_LoadRadius`, `m_ChunksPerFrame` | 로딩 반경(청크 32m), 프레임당 생성 수 |
| `Player/SurvivalState.cs` | 인스펙터 값 | 추위·배고픔·목마름 속도, 모닥불 회복량 |
| `Core/GameManager.cs` | Storms | 첫 눈보라 시각, 간격, 경고 30초, 지속 70초 |
| `Bears/BearSpawner.cs` | `Chance()` | 구역별 곰 출현 확률 |
| `Weather/WeatherController.cs` | Calm / Blizzard | 안개 거리, 눈의 양, 바람 |
| `UI/Strings.cs` | 모든 문구 | 게임 안 한글 텍스트. 폰트가 동적 아틀라스라 새 글자는 실행 중에 자동으로 추가된다 |

폰트: Noto Sans KR (SIL Open Font License 1.1, `Assets/99. External Assets/Font/Noto_Sans_KR/OFL.txt`).

## 헤드셋 없이 화면 확인하기

메뉴 **`Untrodden > Capture Screenshots`** 를 누르면 씬을 만들고, 플레이 모드로 한 판을 자동 진행하며 `Captures/` 폴더에 PNG 11장을 저장한다(월드 지도, 타이틀, 숲, 강, 모닥불과 곰, 아이템, 손목 UI, 눈보라, 사망, 마을, 결과). Unity를 열지 않고 명령줄로도 돌릴 수 있다.

```
Unity.exe -batchmode -projectPath . -executeMethod Untrodden.EditorTools.CaptureRunner.Run -logFile capture.log
```

`Captures/`는 Git에 올라가지 않는다. 시드는 `20261116`으로 고정이라 매번 같은 숲이 찍힌다.

## 문제가 생기면

- 콘솔의 빨간 에러 메시지, 또는 헤드셋에서 이상했던 장면을 그대로 알려주기.
- 화면이 분홍색: URP 셰이더 문제. `Untrodden > Build Game Scene`을 다시 실행.
- 한글이 네모로 나오면: `GameAssets.asset`의 **Font**가 비어 있는지 확인하고 `Build Game Scene`을 다시 실행.
- 콘솔에 `[Untrodden] World seed ...` 로그가 찍힌다. 특정 숲에서 문제가 나면 이 시드를 함께 알려주기.
