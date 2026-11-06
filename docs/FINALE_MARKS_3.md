# 남은 Finale 기호 정리

사용자 요청(2026-11-06): "우선 싹다 만들고 나중에 한번에 고치자." 가져오기에서 아직 버리던 항목을 실제 Finale 악보에 자주 나오는 순서로 묶어, 묶음마다 데이터 모델 → 가져오기·내보내기·저장 → 그리기 → 테스트 순서로 끝낸다. 넣는 도구는 만들지 않는다(가져온 것은 보이고 저장된다). 화면 위치는 Finale 기본 배치를 흉내 낸 것이며, Windows에서 확인한 뒤 한꺼번에 고친다.

## 1. 작은 기호 (스키마 24 → 25)

### 데이터 모델
| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `Articulation` | `Stress`, `Unstress`, `SoftAccent` | 강세·약세·부드러운 악센트. 악센트처럼 음표머리 쪽 보표 바깥에 그린다. |
| `Articulation` | `BreathMark`, `Caesura` | 숨표·중간 휴지. 음표 뒤, 보표 위쪽에 그린다. |
| `Articulation` | `Doit`, `Falloff`, `Plop`, `Scoop` | 재즈 아티큘레이션. 도이트·폴오프는 음표머리 오른쪽, 플롭·스쿱은 왼쪽에 그린다. |
| `Articulation` | `Schleifer`, `Haydn`, `Shake` | 장식음. 트릴처럼 보표 위에 그린다. |
| `ScoreEvent` | `Fingerings` (문자열 목록), `FingeringAbove` | 운지 번호. 바꿔 짚기("4-1")는 두 개로 저장한다. |
| `ScoreEvent` | `AccidentalMark` (−2…2) | 장식음에 붙는 임시표(트릴 위의 ♯), 또는 편집자 임시표 |
| `ScoreEvent` | `BuzzRoll` | 측정하지 않는 트레몰로(버즈 롤): 기둥 위의 "z" |
| `ScoreMeasure` | `BarlineFermata` | 마디를 닫는 마디선 위의 페르마타 |

### MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `articulations/stress`, `unstress`, `soft-accent`, `breath-mark`, `caesura`, `doit`, `falloff`, `plop`, `scoop` | 해당 `Articulation` |
| `ornaments/schleifer`, `haydn`, `shake` | 해당 `Articulation` |
| `technical/fingering` (`placement`) | `Fingerings`, `FingeringAbove` |
| `ornaments/accidental-mark`, `notations/accidental-mark` | `AccidentalMark`. 내보낼 때는 장식음이 있으면 `ornaments` 안에, 없으면 `notations` 바로 아래에 쓴다. |
| `ornaments/tremolo type="unmeasured"` | `BuzzRoll` |
| `barline/fermata` | 오른쪽 마디선이면 그 마디, 왼쪽 마디선이면 앞 마디의 `BarlineFermata` |

### 그리기
- 운지 번호: 화음 위(또는 아래)에 세리프 글자로 한 줄씩 쌓는다. 위에 놓을 때는 맨 위 음의 번호가 맨 위에 온다.
- 장식음 임시표: 장식음 위에 0.7배 크기로 그린다.
- 숨표·중간 휴지·도이트·폴오프가 있으면 음표 뒤에 1.8칸을 더 비운다.
- 마디선 페르마타: 각 파트 첫 보표 위, 마디선 가운데에 그린다.

### 결과 (2026-11-06)
- 새 샘플 `samples/musicxml/finale-marks-3.musicxml`을 경고 없이 가져오고, 저장·다시 열기·내보내기 왕복 결과가 같다. 내보낸 21개 파일이 MusicXML 4.0 스키마를 통과한다.
- 그리기 테스트: 운지 번호 순서와 위치, 숨표 위치, 마디선 페르마타, 버즈 롤(`MarksLayoutTests`).
- `.musx` 220개: 경고 없는 파일 183 → 184.
- "아직 지원하지 않는 기호" 샘플의 슐라이퍼·숨표는 세로 턴(`vertical-turn`)과 기타 아티큘레이션(`other-articulation`)으로 바꿨다.
- 아직 경고하는 것: 세로 턴 등 드문 장식음, 기타 아티큘레이션·주법 기호(`other-*`), 댐프 기호.

## 2. 큐 음표, 숫자 저음 (스키마 25 → 26)

### 데이터 모델
| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `ScoreEvent` | `Cue` | 큐 음표(MusicXML `<cue/>`): 작게 그리고 연주하지 않는다. |
| `ScoreEvent` | `CueSize` | 연주하지만 작게 그리는 음표(`<type size="cue">`) |
| `ScoreMeasure` | `FiguredBass` (`ScoreFiguredBass` 목록) | 보표(파트의 맨 아래 보표), 위치, 지속 길이(없으면 0), 괄호, 숫자들(`ScoreFigure`: 앞 기호, 숫자, 뒤 기호, 연장선) |

### MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `note/cue` | `Cue`. 내보낼 때는 `<cue/>`를 맨 앞에 쓰고 `<tie>`는 쓰지 않는다(스키마 규칙). 붙임줄 그림은 `<tied>`로 남는다. |
| `type size="cue"` | `CueSize` (`Cue`인 음표에도 쓴다) |
| `figured-bass` (`figure`의 `prefix`, `figure-number`, `suffix`, `extend`, `duration`, `parentheses`) | 커서 위치의 `ScoreFiguredBass`. MusicXML 규칙대로 뒤따르는 음표에 붙는다. |

### 그리기
- 큐 음표·큐 쉼표: 음표머리, 임시표, 점, 꼬리를 0.75배로 그리고, 기둥도 0.75배 길이로 그린다(가운데 줄까지 늘이지 않는다). 간격과 빔은 보통 크기와 같다.
- 숫자 저음: 그 보표 아래, 그 줄의 가장 낮은 음표보다 아래에 한 기준선을 두고, 숫자를 위에서 아래로 쌓는다. ♯·♭·♮·+·/·\\는 글자로 쓰고, 연장선은 지속 길이 끝까지 긋는다.

### 결과 (2026-11-06)
- 새 샘플 `samples/musicxml/cue-figured-bass.musicxml`을 경고 없이 가져오고, 저장·다시 열기·내보내기 왕복 결과가 같다. 내보낸 22개 파일이 MusicXML 4.0 스키마를 통과한다.
- 그리기 테스트: 큐 음표 크기, 숫자 저음의 위치와 쌓는 순서(`MarksLayoutTests`).
- 알려진 한계: 큐 음표의 빔은 보통 굵기다. 숫자 저음 연장선은 마디를 넘어가지 않는다.
