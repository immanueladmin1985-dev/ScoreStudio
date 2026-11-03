# 페달, 세뇨·코다

피아노곡과 성가곡 반주에 흔한 페달 표시와, 반복 구조를 나타내는 세뇨·코다 기호를 가져오고, 내보내고, 그린다. 지금까지는 "페달", "세뇨·코다" 경고를 내고 버렸다.

## 조사 (2026-11-03)
- MusicXML에서 페달은 `<direction-type><pedal type="start|stop|change|continue|sostenuto|discontinue|resume" line="yes|no" sign="yes|no"/>`로 쓴다.
  - Finale의 표현식 페달("Ped."와 "*")은 `line="no" sign="yes"`의 start·stop 쌍으로 나온다.
  - Finale의 스마트 도형 페달(갈고리 달린 선)은 `line="yes" sign="no"`이고, 페달을 바꾸는 자리는 `change`로 나온다.
  - `sign`을 쓰지 않았으면 MusicXML 규칙대로 `line="no"`일 때 기호를 쓰고 `line="yes"`일 때 쓰지 않는다.
- 세뇨·코다는 `<direction-type><segno/>`·`<coda/>`로 쓰고, 드물게 마디선(`<barline>`) 안에 쓴다. "D.S. al Coda", "To Coda", "Fine" 같은 글자는 이미 글자 지시로 가져오고 있다.
- denigma는 Finale의 페달 스마트 도형을 위 형식으로 쓰지만, 세뇨·코다 같은 반복 기호는 아직 글자("Segno")로 쓴다. 따라서 `.musx`의 세뇨·코다는 이번 작업으로도 기호가 되지 않는다.

## 데이터 모델 (스키마 23 → 24)
| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `DirectionKind` | `Segno`, `Coda` | 세뇨·코다 기호. 글자는 쓰지 않는다. |
| `LineKind` | `Pedal` | 페달: 시작(밟음)부터 끝(뗌)까지의 `ScoreLine` |
| `ScoreLine` | `PedalSign` | 시작에 "Ped."(소스테누토면 "Sost."), 선이 없으면 끝에 "*"를 그린다. |
| `ScoreLine` | `PedalLine` | 바닥에 선을 긋고 양 끝에 위로 갈고리를 단다. |
| `ScoreLine` | `Sostenuto` | 소스테누토 페달 |
| `ScoreLine` | `PedalChanges` (`ScoreLineChange` 목록) | 페달을 떼었다 바로 밟는 자리: 시작 마디에서의 마디 차이와 위치 |

## MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `pedal type="start"`·`"sostenuto"` ~ `type="stop"` | `LineKind.Pedal` (`Sostenuto`는 sostenuto로 시작한 것) |
| `pedal type="change"` | 열린 페달의 `PedalChanges`. 열린 페달이 없으면 새 페달을 시작한다. |
| `pedal type="continue"` | 줄이 바뀌어 이어지는 표시이므로 무시한다(줄 나눔은 ScoreStudio가 정한다). |
| `pedal type="discontinue"`·`"resume"` | 경고하고 무시한다. |
| `segno`, `coda` (지시 또는 마디선 안) | `DirectionKind.Segno`·`Coda`. 마디선의 것은 그 마디의 처음(왼쪽 마디선) 또는 끝(오른쪽 마디선) 위치에 둔다. |

- 내보내기는 같은 요소로 쓴다. 마디선 안의 세뇨·코다도 지시(`direction`)로 쓴다.
- 끝나지 않은 페달은 경고하고 가져오지 않는다.

## 그리기
- 세뇨·코다: 보표 위, 그 자리의 음표 위에 SMuFL `segno`·`coda` 글리프를 그린다. 다른 글자 지시와 겹치지 않게 옆으로 비킨다.
- 페달: 보표 아래(피아노는 아래 보표). 높이는 그 구간의 가장 낮은 음표·셈여림보다 아래다.
  - 기호식: 시작 음표 아래 "Ped.", 뗄 자리에 "*". 바꾸는 자리에는 "*"와 "Ped."를 다시 쓴다.
  - 선식: 시작에서 위로 갈고리, 바닥 선, 끝에서 위로 갈고리. 바꾸는 자리에는 위로 솟은 "^" 모양을 넣는다.
  - 둘 다면 "Ped." 뒤에서 선이 시작한다.
  - 다음 줄로 이어지면 그 줄 처음부터 선을 다시 그린다(괄호선과 같다).

## 편집
이번 범위에서는 넣는 도구를 만들지 않는다. 가져온 것은 보이고 저장된다(글리산도·괄호선과 같다).

## 수용 기준
1. 새 샘플 `samples/musicxml/pedal-segno-coda.musicxml`(기호식 페달, 바꾸는 자리가 있는 선식 페달, 마디를 넘는 페달, 소스테누토, 지시의 세뇨, 마디선의 코다, "D.S. al Coda" 글자)을 경고 없이 가져오고 값이 원본과 같다. 저장 후 다시 열어도 같다.
2. 버전 23 파일이 열리고, 잘못된 페달 바꿈 자리는 거부한다.
3. 모든 샘플의 내보내기 왕복 결과가 같고, MusicXML 4.0 스키마를 통과한다.
4. 그리기: "Ped."와 "*" 글리프가 보표 아래, 선식 페달의 갈고리와 "^", 세뇨·코다 글리프가 보표 위, 다음 줄로 이어지는 페달(자동 테스트).
5. `.musx` 220개가 모두 열리고, "페달" 경고가 사라진다.

## 결과 (2026-11-03)
1. `pedal-segno-coda.musicxml`을 경고 없이 가져오고 값이 원본과 같으며, 저장 후 다시 열어도 같다(`MusicXmlImporterTests`). — 통과
2. 버전 23 파일이 열리고 잘못된 페달 바꿈 자리를 거부한다(`ProjectFileServiceTests`). — 통과
3. 내보내기 왕복 결과가 같고, 내보낸 20개 파일이 MusicXML 4.0 스키마를 통과한다(xmllint). — 통과
4. 그리기(`MarksLayoutTests`): "Ped."·"*"가 보표 아래, 선식 페달의 갈고리 두 개와 "^", 세뇨가 보표 위, 다음 줄로 이어지는 페달이 두 줄에 그려진다. Windows 화면 그림 테스트에도 새 샘플을 넣었다. — 통과
5. `.musx` 220개가 모두 열리고 "페달" 경고가 사라졌다. 경고 없는 파일은 182개에서 183개로 늘었다. — 통과

함께 바꾼 것: `samples/musicxml/unsupported-features.musicxml`은 "아직 가져오지 않는 기호" 경고를 확인하는 샘플이므로, 이제 가져오는 페달 대신 아직 가져오지 않는 댐프 기호(`damp`)를 넣었다.

알려진 한계
- 페달 높이는 그 구간의 음표만 보고 정한다. 같은 보표 아래에 셈여림이 있으면 겹칠 수 있다(피아노에서는 셈여림이 보통 두 보표 사이에 있어 드물다).
- 같은 자리에서 끝나는 페달과 새로 시작하는 페달은 기호가 붙어 그려진다.
- 마디선에 붙은 코다는 다음 마디 처음에 그린다.
