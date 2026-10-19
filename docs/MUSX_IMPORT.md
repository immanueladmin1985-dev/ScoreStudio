# Finale 파일(.musx) 열기

Finale 호환 작업의 3단계다(`docs/ROADMAP.md`). Finale은 2024년에 개발이 끝나서, 사용자가 가진 `.musx` 원본을 Finale 없이 여는 것이 목표다. `.musx`로 다시 저장하는 것은 목표가 아니다(`CLAUDE.md`).

## 방식: denigma로 MusicXML을 만든 뒤 가져온다
`CLAUDE.md`의 기술 방향("`.musx` 변환은 별도 어댑터/프로세스로 격리한다")대로, ScoreStudio는 `.musx`를 직접 해석하지 않고 별도 프로그램을 실행한다.

1. 원본 `.musx`를 임시 작업 폴더에 복사한다. 원본은 읽기만 하고 절대 쓰지 않는다.
2. 복사본으로 `denigma export <복사본> --musicxml <작업 폴더>\score.musicxml`을 실행한다(시간 제한 2분).
3. 만들어진 MusicXML을 지금의 MusicXML 가져오기(`MusicXmlImporter`)로 읽는다. 가져오기 경고는 그대로 보인다.
4. 프로젝트의 원본 경로(`ImportedFrom`)는 `.musx` 원본을 가리킨다. 작업 폴더는 지운다.

### 외부 프로그램 선택
| 후보 | 상태(2026-10 확인) | 판단 |
|---|---|---|
| [denigma](https://github.com/rpatters1/denigma) (openmusx/denigma) | MIT 라이선스, Robert G. Patterson·Chris Roode, 2026-10에도 활발히 갱신. Finale 없이 `.musx`를 EnigmaXML·MNX·MusicXML로 바꾼다. MusicXML 변환은 "개발 중" 표시. | **채택.** C++ 명령줄 프로그램이라 별도 프로세스로 돌리기 쉽고, Windows에서 빌드된다. |
| [musxdom](https://github.com/rpatters1/musxdom) | MIT, 같은 저자. `.musx` 안의 EnigmaXML을 C++ 객체로 읽는 라이브러리. denigma가 쓴다. | denigma를 통해 간접 사용. |
| C#으로 직접 해석 | `.musx`(ZIP 안의 암호화·압축된 EnigmaXML)를 풀고 Finale 내부 구조를 모두 옮겨야 한다. | 사용하지 않음. 규모가 매우 크고, musxdom이 이미 같은 일을 한다. |

- Windows 배포: CI가 정해진 커밋의 denigma를 Windows에서 빌드해 `tools\denigma\denigma.exe`로 앱 옆에 넣는다. 라이선스는 `docs/THIRD_PARTY.md`에 기록한다.
- 찾는 순서: 프로그램 설정에 고른 경로 → 환경 변수 `SCORESTUDIO_DENIGMA` → 앱 폴더의 `tools\denigma\denigma.exe` → PATH.

## 화면
- "파일 → 가져오기 → Finale 파일(.musx)..."을 켠다. 고른 파일을 변환하는 동안 상태 표시줄에 알린다.
- 결과 창은 MusicXML 가져오기와 같다. 제목에 "Finale 파일"임을 쓰고, denigma의 MusicXML 변환이 개발 중이라는 점을 경고 목록 맨 위에 보인다.
- denigma를 찾지 못하면 무엇이 없는지 한국어로 알리고 현재 악보는 그대로 둔다.

## 수용 기준
### 자동 테스트
1. 찾기: 설정 경로·환경 변수·앱 폴더·PATH 순서를 지킨다.
2. 원본 보호: 변환 전후 원본의 내용과 수정 시각이 같고, 작업 폴더가 남지 않는다.
3. 오류: 없는 파일, 빈 파일, ZIP이 아닌 파일, denigma가 실패하거나 시간을 넘긴 경우 한국어 오류를 낸다.
4. 실제 denigma(`SCORESTUDIO_DENIGMA`가 있을 때, CI에서는 필수): `samples/musx/`의 시험 파일(denigma·musxdom 저장소의 MIT 시험 파일)이 오류 없이 열리고, 음표가 있다.


### 수동 테스트 (Windows PC)
1. 사용자가 가진 실제 `.musx` 파일(성가곡, 피아노곡, 합창곡)을 열어 Finale에서 본 악보와 비교한다.

## 함께 고친 가져오기 (스키마 17 → 18)
변환 결과에서 많이 나온 경고 두 가지를 MusicXML 가져오기에서 고쳤다.
- **이조 악기**(`transpose`, 23개 파일): 버리던 값을 `ScorePart.Transpose`(`Diatonic`, `Chromatic`, `OctaveChange`)로 저장하고 MusicXML로 다시 쓴다. 음표는 지금처럼 기보된 음높이로 보인다. 다시 내보낸 악보가 Finale에서 제 음높이로 소리 나게 하려는 것이다. 곡 중간에 바뀌면 처음 것만 남기고 경고한다.
- **빠르기 표시 안의 음표 기호**(`symbol`, 25개 파일): Finale은 "Tempo (♩=120)"을 글자 + SMuFL 음표(`metNoteQuarterUp`) + 글자로 쓴다. 음표를 ♩·♪ 같은 글자로 바꿔 한 글자 지시로 합친다.

## 시험 결과 (2026-10-19, denigma 4.0.0 커밋 5a4ae8e, Linux 빌드)
denigma·musxdom 저장소의 공개 시험 파일 **220개를 모두** 변환해 가져왔다.

| 항목 | 결과 |
|---|---|
| 성공 / 실패 | 220 / 0 |
| 경고 없는 파일 | 139 (위의 두 가지를 고치기 전에는 119) |
| 음표 수 합계 | 3,406 |
| 실제 곡 | `zwei_gesange.musx`(Brahms, Op. 91: 알토·비올라·피아노, 99마디)가 셈여림·이음줄·글자 지시·빠르기와 함께 그려진다. |

남은 경고(파일 수 순): ~~꾸밈음 17~~(스키마 19에서 가져옴: `docs/GRACE_NOTES.md`, 이후 경고 없는 파일 149개), ~~울림 붙임줄 16, 장식음 기호(트릴 등) 11~~(스키마 20에서 가져옴: `docs/ORNAMENTS.md`, 이후 171개), ~~글리산도 4, 괄호선 지시 4, 리허설 마크 3, 주법 기호 3, 아르페지오 3, 트릴 연장선 2, 점선 지시 1~~(스키마 21·22에서 가져옴: `docs/FINALE_MARKS_2.md`, 이후 181개), 박자표보다 긴 마디 6, 타악기 음자리표 5, 곡 중간의 이조 변경 5, 일반적이지 않은 조표 3, 지시 기호(symbol) 3, 코드 다이어그램 2, 꾸밈음에 걸린 글리산도 2, 복합 박자 표기 2. 이 목록이 다음 Finale 호환 작업의 순서가 된다.

알려진 한계
- 피아노에서 위아래 보표를 잇는 이음줄은 사선처럼 그려진다(`docs/SPRINT_FINALE_MARKS.md`).
- 결과는 denigma의 MusicXML 변환 품질에 달려 있다. denigma가 빠뜨린 것(예: 일부 코드 이름)은 ScoreStudio에도 없다.
