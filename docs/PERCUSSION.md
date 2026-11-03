# 타악기 보표: 타악기 음자리표, 음높이 없는 음표, 음표머리 모양, 단선 보표

`.musx` 시험 파일 220개를 가져올 때 남은 경고 가운데 "지원하지 않는 음자리표(PERCUSSION)"가 5개 파일로 가장 많았다. Finale에서 내보낸 MusicXML의 드럼·타악기 파트도 지금은 음표가 모두 빠진다("타악기 음표(unpitched)" 경고). 성가대·관현악 악보에도 팀파니 외의 타악기 파트가 흔하므로 Finale 호환에 필요하다.

## 조사 결과 (2026-11-03)
- MusicXML 4.0 규칙
  - 타악기 음자리표의 음표는 `<pitch>`가 아니라 `<unpitched>`로 쓴다.
  - `display-step`·`display-octave`는 높은음자리표 기준의 보표 위치다(G4가 둘째 줄). 둘 다 없으면 보표의 가운데 줄에 놓는다.
  - 한 파트에 악기가 여럿이면(드럼 세트) `<score-instrument>`를 여러 개 두고, 음표마다 `<instrument id>`로 가리킨다. 소리는 `<midi-instrument>`의 `<midi-unpitched>`로 정한다.
  - 줄 수는 `<staff-details><staff-lines>`로 정한다(단선 보표는 1).
  - 음표머리 모양은 `<notehead>`로 정한다(x, circle-x, diamond, triangle, slash 등).
- denigma(.musx 변환)의 한계
  - denigma 4.0.0은 타악기 보표의 음표를 `<unpitched>`가 아니라 Finale 내부 값을 그대로 `<pitch>`로 쓴다(`drumset.musx`에서 C2, F5, D#5). denigma 저장소의 `src/formats/musicxml/roadmap.md`의 "Percussion" 항목에 미구현으로 적혀 있다.
  - 따라서 `.musx`의 타악기 음표 위치는 이번 작업으로도 원본과 같아지지 않는다. 음자리표·줄 수·음표머리 모양은 맞게 들어온다.

## 범위
이번 작업은 **MusicXML의 타악기 표기를 빠짐없이 가져오고, 내보내고, 그리는 것**이다. `.musx`의 타악기 음표 위치는 별도 작업으로 남긴다(아래 "다음 단계").

## 데이터 모델 (스키마 22 → 23)
| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `ClefType` | `Percussion` | 타악기 음자리표. 음표 위치는 높은음자리표와 같이 계산한다. |
| `ScoreStaff` | `Lines` (기본 5, 1~5) | 보표의 줄 수. 줄을 줄이면 가운데 줄을 중심으로 남긴다. |
| `ScorePart` | `Instruments` (`ScoreInstrument` 목록) | 파트의 악기 목록: `Id`, `Name`, `MidiChannel`, `MidiProgram`, `MidiUnpitched`(1~128, 없으면 null). 악기가 둘 이상이거나 `midi-unpitched`가 있을 때만 채운다. 그 밖의 파트는 지금처럼 `Instrument` 이름 하나만 쓴다. |
| `ScoreEvent` | `Unpitched` | 음높이 없는 음표. `NotationSpelling`은 보표 위치(예: "C5")이고 임시표가 없다. `MidiPitch`는 악기의 `MidiUnpitched` − 1, 없으면 보표 위치의 음높이다. |
| `ScoreEvent` | `Instrument` | 음표가 가리키는 악기 `Id`(없으면 빈 문자열) |
| `ScoreEvent` | `Notehead` (`NoteheadShape`) | `Normal`, `X`, `CircleX`, `Diamond`, `Triangle`, `InvertedTriangle`, `Square`, `Slash`, `Cross`. 음높이가 있는 음표에도 쓸 수 있다(말하는 음표의 x 등). |

저장 파일을 열 때 줄 수, 악기 목록, 악기 참조가 올바르지 않으면 한국어 오류를 낸다.

## MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `clef/sign` = `percussion` | `ClefType.Percussion` |
| `staff-details/staff-lines` | `ScoreStaff.Lines`. 곡 중간에 바뀌면 처음 값만 쓰고 경고한다. 6줄 이상(타브 등)은 경고한다. |
| `note/unpitched` (`display-step`, `display-octave`) | `Unpitched` + `NotationSpelling`. 위치가 없으면 가운데 줄(B4). |
| `note/instrument id` | `Instrument` |
| `score-instrument`, `midi-instrument`(`midi-channel`, `midi-program`, `midi-unpitched`) | `ScorePart.Instruments` |
| `note/notehead` | `Notehead`. 목록에 없는 모양(do·re·mi 모양 음표 등)은 보통 머리로 두고 경고한다. `smufl="noteheadTriangleUpBlack"`처럼 `other`와 SMuFL 이름이 함께 오면 그 이름으로 모양을 정한다. |
| 타악기 음자리표 보표에 `<pitch>`로 적힌 음표(denigma 출력) | 그 음높이를 보표 위치로 쓰는 음높이 없는 음표로 가져오고, "위치가 원본과 다를 수 있음"을 한 번 경고한다. |

- 내보내기는 같은 요소로 쓴다. 음높이 없는 음표에는 `<pitch>` 대신 `<unpitched>`를 쓰고, `Instruments`가 있으면 `score-instrument`와 `midi-instrument`를 모두 쓴다.

## 그리기
- 타악기 음자리표: SMuFL `unpercussionClef1`(두 세로 막대)을 보표 가운데에 그린다.
- 타악기 음자리표 보표에는 조표를 그리지 않고(Finale와 같다), 음높이 없는 음표에는 임시표를 그리지 않는다.
- 줄 수: 1줄이면 가운데 줄만, 3줄이면 가운데 세 줄을 그린다. 보표의 높이와 간격은 5줄 보표와 같게 둔다. 줄이 5개가 아닌 보표에는 덧줄을 그리지 않는다.
- 마디선: 1줄 보표에서는 줄 위아래 1칸씩만 긋는다(Finale 기본 모양).
- 음표머리: 모양과 음길이(검은 머리·2분·온음표)에 맞는 SMuFL 글리프를 쓰고, 기둥은 그 글리프의 폭에 붙인다.

## 편집
- 타악기 음자리표 보표에 음표를 넣으면 음높이 없는 음표가 된다(누른 위치가 보표 위치). 반음 올리기·내리기는 거절한다.
- 편집 메뉴의 음자리표 바꾸기에 "타악기 음자리표"를 넣는다. 음자리표를 바꿔도 이미 있는 음표는 그대로 둔다(Finale와 같다).
- 음표머리 모양과 악기를 고르는 도구는 이번 범위가 아니다. 가져온 값은 보이고 저장된다.

## 수용 기준
1. 새 샘플 `samples/musicxml/percussion.musicxml`(Finale식 드럼 세트 5줄 보표: 베이스 드럼, 스네어, 하이햇 x머리, 크래시 x머리, 탐탐 / 단선 보표의 트라이앵글·우드블록, 서로 다른 음표머리)을 경고 없이 가져오고, 값이 원본과 같다.
2. 저장 후 다시 열어도 같고, 버전 22 파일은 줄 수 5, 보통 머리, 음높이 있는 음표로 열린다. 잘못된 값은 거부한다.
3. 모든 샘플의 내보내기 왕복 결과가 같고, 내보낸 파일이 MusicXML 4.0 스키마를 통과한다.
4. 그리기: 타악기 음자리표 글리프, 1줄 보표에 줄 하나와 짧은 마디선, x머리 글리프, 조표·임시표·덧줄 없음(자동 테스트).
5. 편집: 타악기 보표에 넣은 음표가 음높이 없는 음표가 되고, 음자리표 메뉴에 타악기 음자리표가 있다(자동 테스트).
6. `.musx` 시험 파일 220개: 모두 열리고, 타악기 음자리표 경고가 사라진다. 타악기 음표는 denigma 한계 경고로 바뀐다.

## 결과 (2026-11-03)
1. `percussion.musicxml`을 경고 없이 가져오고, 음자리표·줄 수·위치·음표머리·악기·MIDI 값이 원본과 같다(`MusicXmlImporterTests`). — 통과
2. 저장 후 다시 열어도 같고, 버전 22 파일이 열리며, 잘못된 줄 수·악기·음표머리를 거부한다(`ProjectFileServiceTests`). — 통과
3. 모든 샘플의 내보내기 왕복 결과가 같고, 내보낸 19개 파일이 MusicXML 4.0 스키마를 통과한다(xmllint). — 통과
4. 그리기(`MarksLayoutTests`): 타악기 음자리표가 가운데 줄에 있고, 조표·임시표가 없으며, x머리가 높은음자리표 위치에 그려진다. 1줄 보표에는 줄 하나, 위아래 1칸씩의 마디선이 있고 덧줄이 없다. 1줄 보표의 온쉼표는 줄에 매달린다. — 통과. Windows 화면 그림 테스트에도 새 샘플을 넣었다(`PageRenderingTests`).
5. 편집(`ScoreEditorTests`): 타악기 보표에 넣은 음은 조표와 상관없이 음높이 없는 음표가 되고, 반음 이동·딴이름한소리는 거절하며, 위·아래 이동은 된다. — 통과
6. `.musx` 220개: 모두 열리고, "지원하지 않는 음자리표(PERCUSSION)" 경고(5개 파일)가 사라졌다. 경고 없는 파일은 181개에서 182개로 늘었다. 드럼 음표가 있는 3개 파일에는 "위치가 원본과 다를 수 있음" 경고가 남는다(denigma 한계). — 통과

알려진 한계
- 음표머리는 보통 머리 자리의 가운데에 그린다. Finale처럼 모양마다 기둥 붙는 자리를 따로 맞추지는 않는다(폭 차이 0.2칸 이내, 빗금 머리는 더 넓다).
- 곡 중간에 줄 수가 바뀌는 보표(Finale 보표 스타일)는 처음 줄 수로만 그린다(시험 파일 1개).
- 조표 자리는 다른 파트와 맞추기 위해 비워 둔다(그리지는 않는다).

## 다음 단계 (이번 범위 밖)
- `.musx`의 타악기 음표 위치: denigma가 지원하기를 기다리거나, denigma의 EnigmaXML 출력에서 Finale 타악기 지도(percussion map)를 읽어 위치를 고치는 어댑터를 만든다. 어느 쪽이 나은지는 실제 사용자 파일에 타악기가 얼마나 있는지 보고 정한다.
- 음표머리 모양·악기를 바꾸는 편집 도구(Finale의 Special Tools·Percussion Map)
