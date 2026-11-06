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

## 3. 박자표·조표·이조 (스키마 26 → 27)

### 데이터 모델 (`ScoreMeasure`)
| 추가 항목 | 설명 |
|---|---|
| `TimeSymbol` (`Normal`, `Common`, `Cut`, `SingleNumber`) | 박자표를 숫자, C, ¢, 위 숫자 하나 중 무엇으로 보이는지 |
| `BeatsText` | "3+2"처럼 적힌 위 숫자(박자 계산은 합한 값 `Numerator`로 한다). 보통은 빈 문자열 |
| `SenzaMisura` | 박자 없음: 박자표를 그리지 않는다. 마디 길이는 앞 박자를 따르고, 넘치는 음표는 "박자표보다 긴 마디"처럼 그대로 둔다. |
| `KeyAccidentals` (`ScoreKeyAccidental`: 음이름, 변화) | 올림표·내림표가 차례대로가 아닌 조표(F♯과 B♭ 등). 이때 `KeyFifths`는 0이다. 올림표가 8개 이상인 이론상의 조표(fifths 9 등)도 겹올림표가 섞인 이 형태로 가져온다. |
| `Transpose` | 이 마디부터 바뀌는 이조 악기 설정(예: B♭ 클라리넷 → A 클라리넷). 첫 설정은 지금처럼 `ScorePart.Transpose`에 둔다. |

### MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `time symbol="common"·"cut"·"single-number"` | `TimeSymbol` |
| `beats`의 "3+2" (박자 종류가 하나일 때) | `BeatsText`. 여러 박자를 이은 박자표(3/4+2/8)는 여전히 합한 박자로 가져오고 경고한다. |
| `time/senza-misura` | `SenzaMisura` |
| `key`의 `key-step`·`key-alter` 쌍, 또는 fifths가 ±8~±14인 조표 | `KeyAccidentals` |
| 곡 중간의 다른 `transpose` | 그 마디의 `Transpose` |

- 다음 마디로 이어지는 값(박자 기호, 다른 조표)은 MusicXML처럼 바뀔 때까지 유지한다. 편집에서 박자표나 조표를 새로 정하면 숫자 박자표, 보통 조표로 돌아간다.
- 음표의 임시표 계산(화면, 내보내기, 음표 넣기)은 다른 조표의 변화음을 따른다.

### 그리기
- C·¢: SMuFL `timeSigCommon`·`timeSigCutCommon`을 가운데 줄에 그린다. 위 숫자 하나: 가운데 줄에 숫자만 그린다. "3+2": 숫자와 `timeSigPlus`를 이어 그린다.
- 다른 조표: 각 변화음을 그 음의 올림표(내림표는 내림표) 자리에 적힌 순서대로 그린다.

### 결과 (2026-11-06)
- 새 샘플 `samples/musicxml/time-key-transpose.musicxml`을 경고 없이 가져오고, 저장·다시 열기·내보내기 왕복 결과가 같다. 내보낸 23개 파일이 MusicXML 4.0 스키마를 통과한다.
- 그리기 테스트: C·¢·+ 글리프, 박자 없음 마디에 박자표 없음, 다른 조표의 위치(`MarksLayoutTests`). 편집 테스트: 다른 조표의 변화음을 따라 음표가 들어가고, 박자표를 새로 정하면 숫자 박자표가 된다.
- `.musx` 220개: 경고 없는 파일 184 → 194.
- 알려진 한계: 다른 조표에서 보통 조표로 바뀔 때 제자리표로 지우는 표시를 그리지 않는다. 이조 변경은 저장·내보내기만 하고 화면 표시는 바뀌지 않는다(음표는 기보음대로).

## 4. 코드 다이어그램, 지시 기호(symbol) (스키마 27 → 28)

### 데이터 모델
| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `ScoreChordSymbol` | `Frame` (`ScoreFrame`) | 기타 코드 다이어그램: 줄 수, 프렛 수, 시작 프렛(줄머리에서 시작하면 null), 짚는 음(`ScoreFrameNote`: 줄 번호(1이 가장 높은 줄), 프렛(0은 개방현), 손가락 번호, 바레 시작·끝). 음이 없는 줄은 치지 않는 줄이다. |
| `DirectionKind` | `Symbol` | 음악 글꼴 기호 하나. `Text`에 SMuFL 이름("keyboardPedalPed")을 둔다. |

### MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `harmony/frame` (`frame-strings`, `frame-frets`, `first-fret`, `frame-note`의 `string`, `fret`, `fingering`, `barre`) | `Frame`. 짚는 음이 하나도 없거나 값이 범위를 벗어나면 경고하고 버린다. |
| `direction-type/symbol` (빠르기 표시 안의 음표 기호가 아닌 것) | `DirectionKind.Symbol`. ScoreStudio가 그릴 줄 모르는 이름은 저장·내보내기만 하고 "그리지 않음"을 경고한다. |

### 그리기
- 코드 다이어그램: 코드 이름 위에 줄 간격 0.8칸, 프렛 간격 0.9칸의 격자를 그린다. 줄머리에서 시작하면 맨 위 줄을 굵게, 아니면 오른쪽에 "5fr"를 쓴다. 짚는 자리에 점, 개방현 위에 o, 치지 않는 줄 위에 x, 바레는 굵은 선, 손가락 번호는 격자 아래에 쓴다.
- 지시 기호: Bravura의 글리프 표(`Smufl.Named`: 페달 Ped·*, 8va, 세뇨·코다, 페르마타, 마디 반복, 숨표, 음자리표, 트라이앵글 등 약 40개)에 있는 것만 다른 지시처럼 보표 위(또는 아래)에 그린다.

### 결과 (2026-11-06)
- 새 샘플 `samples/musicxml/chord-diagrams-symbols.musicxml`을 경고 없이 가져오고, 저장·다시 열기·내보내기 왕복 결과가 같다. 내보낸 24개 파일이 MusicXML 4.0 스키마를 통과한다.
- 그리기 테스트: 다이어그램의 점·o·x·줄 수와 위치, 보표 아래의 페달 기호(`MarksLayoutTests`).
- `.musx` 220개: 경고 없는 파일 194 → 199.
- 알려진 한계: 다이어그램이 높으면 같은 줄의 다른 위쪽 지시들이 그 위로 올라간다. 시스템 사이 간격은 다이어그램 높이를 일부만 반영한다.

## 남은 경고 (2026-11-06, `.musx` 220개 기준)
남은 것은 대부분 원본 파일의 상태를 알리는 경고다(박자표보다 긴 마디, 같은 절의 가사 두 개, 읽을 수 없는 빔·박자표, 길이 없는 음표). 기능으로 남은 것은 다음과 같다.
- denigma 한계: 타악기 음표 위치, 세뇨·코다 같은 반복 기호를 글자로 씀
- 꾸밈음에 걸린 글리산도, 쉼표·타악기 꾸밈음
- 옥타브 음자리표 일부(낮은음 8vb, 높은음 8va), 여러 박자를 이은 박자표, 곡 중간에 바뀌는 보표 줄 수, 기타 주법 기호(`other-technical`)
