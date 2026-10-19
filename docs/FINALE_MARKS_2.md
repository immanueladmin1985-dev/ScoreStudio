# 남은 Finale 기호: 아르페지오, 주법 기호, 리허설 마크, 글리산도, 괄호선·점선·트릴 연장선

`.musx` 시험 파일 220개를 가져올 때 장식음 다음으로 남은 경고들을 한 묶음으로 처리했다(`docs/MUSX_IMPORT.md`): 글리산도 4개 파일, 괄호선 지시 4, 리허설 마크 3, 주법 기호 3, 아르페지오 3, 트릴 연장선 2, 점선 지시 1.

## 데이터 모델
### A (스키마 20 → 21)
| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `Articulation` | `Harmonic`, `UpBow`, `DownBow`, `Open`, `Stopped`, `SnapPizzicato` | 현악기·관악기 주법 기호. 다른 아티큘레이션과 같은 목록에 둔다. |
| `ScoreEvent` | `Arpeggio` (`None`, `Wavy`, `Up`, `Down`, `NonArpeggio`) | 화음 앞의 아르페지오(물결선, 위·아래 화살표)와 논아르페지오 괄호 |
| `DirectionKind` | `Rehearsal` | 리허설 마크. 글자는 비어 있을 수 없다. |
| `ScoreDirection` | `Boxed` (기본값 참) | 리허설 마크를 네모로 두르는지 |

### B (스키마 21 → 22)
| 대상 | 항목 | 설명 |
|---|---|---|
| `ScoreMeasure.Glissandos` | `ScoreGlissando` | 시작 화음(보표, 성부, 위치)에서 끝 화음(마디 차이, 보표, 성부, 위치)까지. `Wavy`(물결선), `Slide`(MusicXML `slide`), `Text`("gliss." 등) |
| `ScoreMeasure.Lines` | `ScoreLine` | `Kind`(`Bracket`, `Dashes`, `TrillLine`), 보표, 시작 위치, 끝(마디 차이, 위치), `Above`, `Y`, `Dashed`(점선 괄호), `StartHook`·`EndHook`(1 위, −1 아래, 0 없음) |

저장 파일을 열 때 위치·보표·성부·갈고리 값이 올바르지 않으면 한국어 오류를 낸다.

## MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `technical/harmonic`, `up-bow`, `down-bow`, `open`, `stopped`, `snap-pizzicato` | 해당 `Articulation`. 운지 번호와 그 밖의 주법 기호는 여전히 경고한다. |
| `arpeggiate` (`direction="up"`·`"down"`), `non-arpeggiate` | `Arpeggio`. 내보낼 때는 표시가 있는 음마다 `arpeggiate`를 쓰고, `non-arpeggiate`는 맨 위 음에 `type="top"`, 나머지 음에 `type="bottom"`으로 쓴다. |
| `direction-type/rehearsal` (`enclosure="none"`이면 네모 없음) | `DirectionKind.Rehearsal` |
| `glissando`, `slide` (`type="start"`·`"stop"`, `number`, `line-type="wavy"`) | `ScoreGlissando`. 같은 이름과 번호의 start와 stop을 잇는다. |
| `direction-type/bracket` (`line-end`, `line-type`) | `LineKind.Bracket` |
| `direction-type/dashes` | `LineKind.Dashes`. 같은 지시에 있는 글자("cresc.")는 글자 지시로 따로 남는다. |
| `ornaments/wavy-line` | `LineKind.TrillLine` |

- 내보내기는 같은 요소로 쓴다. 번호는 이음줄처럼 겹치는 범위마다 따로 붙인다.
- 꾸밈음에서 시작하거나 꾸밈음에서 끝나는 글리산도는 가져오지 않고 경고한다. 지금 모델은 꾸밈음을 글리산도의 끝점으로 가리킬 수 없기 때문이다(denigma 시험 파일 2개).

## 그리기
- 아르페지오: 화음의 맨 아래 음보다 조금 아래에서 맨 위 음보다 조금 위까지, 임시표 왼쪽에 반 칸씩 꺾이는 물결선을 그린다. 방향이 있으면 끝에 화살표를 붙인다. 논아르페지오는 각진 괄호로 그린다. 화음 앞에 1.2칸을 비워 둔다.
- 주법 기호: 트릴처럼 보표 바깥쪽(위 성부는 위)에 그린다.
- 리허설 마크: 보표 위에 2.6칸 크기의 굵은 글자로 쓰고, 네모를 두른다(`Boxed`).
- 글리산도·슬라이드: 첫 화음의 맨 위 음표머리 오른쪽에서 끝 화음의 맨 위 음표머리 왼쪽(임시표가 있으면 그 왼쪽)까지 곧은 선이나 물결선을 그린다. 줄을 넘어가면 줄 끝에서 멈춘다. "gliss." 같은 글자는 저장하고 내보내지만 아직 화면에 쓰지 않는다.
- 괄호선: 보표 위(또는 아래)에 실선이나 점선을 긋고, 양 끝에 1칸 길이의 갈고리를 단다. 마지막 음을 덮도록 1.3칸 더 그린다.
- 점선 지시: 같은 자리의 글자(cresc. 등) 바로 오른쪽에서 시작하는 점선이다.
- 트릴 연장선: 트릴 기호 오른쪽에서 시작하는 물결선이다.
- 괄호선·점선·트릴 연장선은 다음 줄로 이어지면 다음 줄 처음부터 다시 그린다. 갈고리는 실제로 끝나는 쪽에만 단다.
- 아티큘레이션 도구 메뉴에 주법 기호 6개가 함께 나온다. 글리산도·괄호선을 넣는 도구는 아직 없다(가져온 것은 보이고 저장된다).

## 수용 기준과 결과 (2026-10-19)
1. 새 샘플 `samples/musicxml/finale-marks-2.musicxml`(아르페지오 3종, 하모닉, 올림활·내림활, 리허설 마크 2종, 물결선 글리산도, 슬라이드, 갈고리 괄호선, cresc. 점선, 트릴 연장선)을 경고 없이 가져오고, 값이 원본과 같다. 저장 후 다시 열어도 같다. — 통과
2. 버전 20 파일은 새 항목이 비어 있는 상태로 열리고, 새 항목은 저장 후 다시 열어도 보존된다. 잘못된 값은 거부한다(`ProjectFileServiceTests`). — 통과
3. 모든 샘플의 내보내기 왕복 결과가 같고, 내보낸 18개 파일이 MusicXML 4.0 스키마를 통과한다(xmllint). — 통과
4. 그리기: 아르페지오가 화음 왼쪽에서 음역을 덮고, 글리산도가 두 음을 이으며, 괄호선의 갈고리가 아래를 향하고, 트릴 연장선이 트릴 기호 뒤에서 시작하며, 괄호선이 다음 줄로 이어진다(`MarksLayoutTests`). 화면 그림 테스트에 새 샘플을 추가했다(`PageRenderingTests`, Windows CI). — Linux에서 통과, Windows는 CI에서 확인
5. `.musx` 시험 파일 220개: 모두 열리고, 경고 없는 파일이 171개에서 181개로 늘었다. — 통과

## 알려진 한계
- 글리산도 글자("gliss.", "port.")를 화면에 그리지 않는다.
- 꾸밈음에 걸린 글리산도는 빠진다.
- 줄을 넘어가는 글리산도는 다음 줄에서 이어 그리지 않는다.
- 위치는 Finale의 기본 배치를 흉내 낸 값이다. 원본에서 손으로 옮긴 높이(`default-y`)는 괄호선·트릴 연장선에만 쓴다.
