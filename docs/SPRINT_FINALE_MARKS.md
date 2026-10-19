# Finale 기호: 셈여림·글자 지시·메트로놈·크레셴도·이음줄·아티큘레이션

Finale 호환 작업의 2단계다(`docs/ROADMAP.md`의 "Finale 호환"). 사용자가 Finale에서 내보낸 실제 파일("다윗의 노래", `docs/SPRINT3_MUSICXML.md`)에서 많이 나온 순서대로 넣는다: 아티큘레이션 596, 이음줄 46, 셈여림 61, 크레셴도 37, 글자 지시 27, 페르마타 8, 메트로놈 3.

- **2-A (이 문서의 1차 범위):** 데이터 모델, MusicXML 가져오기·내보내기, 프로젝트 저장·열기. 화면에는 아직 그리지 않는다.
- **2-B:** 악보 화면에 그리기.
- **2-C:** 도구 팔레트와 단축키로 넣고 지우기 (Finale의 Articulation·Expression·Smart Shape 도구).

## 데이터 모델 (스키마 16 → 17)
음악적 값과 원본 높이만 저장하고, 화면 좌표는 저장하지 않는다.

| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `ScoreEvent` | `Articulations: List<Articulation>` | `Staccato`, `Staccatissimo`, `Accent`, `Marcato`(강한 악센트), `Tenuto`, `Fermata`. 겹치지 않게 enum 순서로 저장한다. 화음에서는 화음 전체에 붙는다. |
| `ScoreMeasure` | `Directions: List<ScoreDirection>` | 마디 안 위치(4분음표 단위 분수)와 보표에 놓인 기호. `Kind`: `Dynamic`(셈여림 글자, 예: `mf`, `sfz`), `Words`(글자, `Italic`·`Bold`), `Tempo`(메트로놈: `BeatUnit`, `BeatUnitDots`, `PerMinute`). `Above`(null이면 셈여림은 아래, 나머지는 위), 원본 높이 `Y`. |
| `ScoreMeasure` | `Hairpins: List<ScoreHairpin>` | 시작하는 마디에 둔다. `Crescendo`, 시작 위치, `EndMeasureOffset`(0이면 같은 마디), 끝 위치, `Above`, `Y`. |
| `ScoreMeasure` | `Slurs: List<ScoreSlur>` | 시작하는 마디에 둔다. 시작 화음(보표·성부·위치)과 끝 화음(`EndMeasureOffset`, `EndStaff`, `EndVoice`, 끝 위치), `Above`(null이면 자동). |

- 버전 1~16 파일은 그대로 열리고 새 항목은 빈 목록이 된다.
- 잘못된 값(없는 보표, 음수 위치·길이, 알 수 없는 종류, 빈 셈여림 글자, 메트로놈의 빈 빠르기)은 손상 파일로 거절한다.

## MusicXML 대응
| MusicXML | ScoreStudio |
|---|---|
| `notations/articulations`: `staccato`, `staccatissimo`, `accent`, `strong-accent`, `tenuto`, `detached-legato`(테누토+스타카토), `spiccato`(스타카티시모로, 경고) | `Articulations` |
| `notations/fermata` | `Fermata` (모양은 보통 페르마타로) |
| `notations/slur` (`start`/`stop`, `number`, `placement`) | `ScoreSlur`. 같은 번호의 시작과 끝을 짝짓는다. 같은 마디에서 끝이 시작보다 먼저 나오는 경우(다른 보표로 가는 이음줄)도 짝짓는다. |
| `direction/dynamics` (`p`, `mf`, `sfz` 등 요소 이름, `other-dynamics` 글자), `notations/dynamics` | `Dynamic` |
| `direction/words` (`font-style="italic"`, `font-weight="bold"`, 여러 개는 이어 붙임) | `Words` |
| `direction/metronome` (`beat-unit`, `beat-unit-dot`, `per-minute`) | `Tempo`. 빠르기 바꿈 표시(beat-unit 2개)는 경고하고 가져오지 않는다. |
| `direction/wedge` (`crescendo`/`diminuendo`/`stop`, `number`) | `ScoreHairpin` |
| `direction`의 `offset`, `staff`, `placement`, 기호의 `default-y` | 위치, 보표, `Above`, `Y` |

- 내보내기는 같은 요소로 다시 쓴다. 이음줄·크레셴도의 번호는 마디 단위로 겹치는 것끼리 다르게 매긴다.
- 끝이 없는 이음줄·크레셴도, 시작이 없는 끝은 경고하고 가져오지 않는다.
- 다른 기호(장식음 기호, 주법 기호, 글리산도, 페달, 세뇨·코다, 리허설 마크, 점선 지시 등)는 지금처럼 경고한다.

## 수용 기준 (2-A)
### 자동 테스트
1. 새 샘플 `samples/musicxml/finale-marks.musicxml`: 아티큘레이션 6종과 테누토+스타카토, 페르마타, 마디를 넘는 이음줄과 다른 보표로 가는 이음줄, 셈여림(`mf`, `sfz`, `other-dynamics`), 이탤릭 글자, 메트로놈(점4분음표 포함), 크레셴도·디크레셴도(마디를 넘는 것 포함)를 경고 없이 가져오고 값이 원본과 같다.
2. 이 샘플을 포함한 모든 샘플이 내보내기 → 다시 가져오기 후 같다(1단계 테스트에 자동 포함).
3. 내보낸 파일이 MusicXML 4.0 스키마 검사를 통과한다(수동, xmllint).
4. 저장 후 다시 열면 새 항목이 모두 같고, 버전 16 파일이 열리며, 잘못된 값은 손상 파일로 거절한다.
5. `unsupported-features.musicxml`의 경고에서 가져오게 된 항목(셈여림, 아티큘레이션, 이음줄 등)이 빠지고 나머지는 남는다.

### 수동 테스트 (Windows PC)
1. Finale에서 내보낸 MusicXML(예: "다윗의 노래")을 가져온 뒤 경고 목록에서 위 항목이 사라졌는지 확인한다.
2. 다시 MusicXML로 내보내 Finale에서 열고 셈여림·이음줄·아티큘레이션이 남아 있는지 확인한다.

## 검증 결과 (2-A, 2026-10-19)
- 자동 테스트: `ScoreStudio.Core.Tests` 512개 통과. 새 샘플의 가져오기 값 비교, 모든 샘플(14개)의 내보내기 왕복, 저장·재열기, 버전 16 파일 열기, 잘못된 값 11종 거절.
- 스키마 검사: 내보낸 파일 15개와 새 샘플이 MusicXML 4.0 공식 XSD 검사를 통과했다(xmllint, 수동).
- 아직 확인하지 않음: Windows PC에서 실제 Finale 파일로 확인(수동 테스트 1·2).

## 2-B 그리기 규칙
레이아웃 엔진(`ScoreStudio.Core/Layout/ScoreLayoutEngine.Marks.cs`)이 정한다. 화면(WPF)은 그리기 항목만 그린다.

| 기호 | 규칙 |
|---|---|
| 스타카토·스타카티시모·테누토 | 기둥 반대쪽 음표머리 바깥(두 성부면 위 성부는 위, 아래 성부는 아래). 보표 안이면 줄이 아닌 칸 가운데. 음표에 가까운 것부터 스타카토, 스타카티시모, 테누토. |
| 악센트·마르카토 | 같은 쪽, 그 바깥. 보표 안에 들어가면 보표 밖으로 낸다. |
| 페르마타 | 보표 위, 음표와 다른 기호보다 위(아래 성부는 보표 아래). 이음줄은 페르마타 안쪽으로 지나간다. |
| 이음줄 | 시작·끝 화음의 기둥이 모두 위면 아래, 아니면 위(두 성부면 성부 쪽). `Above`가 있으면 따른다. 사이의 음표·기둥·기호를 넘도록 높이를 키운다. 다음 줄로 넘어가면 줄 끝까지 그리고 다음 줄 처음부터 이어 그린다. |
| 셈여림 | 음악 글꼴의 셈여림 글자(p, m, f, r, s, z, n). 다른 글자가 섞이면 이탤릭 글자. 음표머리 가운데에 맞춘다. |
| 크레셴도·디크레셴도 | 셈여림과 같은 줄. 같은 위치의 셈여림 뒤에서 시작하고, 끝 위치의 셈여림 앞에서 끝난다. 마디 끝에서 끝나면 마디선 조금 앞까지. 다음 줄로 넘어가면 반쯤 벌어진 채로 이어진다. |
| 셈여림·크레셴도의 높이 | 원본 높이(`Y`)가 있으면 그대로. 없으면 보표마다 한 줄: 보표 아래, 그 줄의 가장 낮은 음표보다 아래. 가사가 있는 보표에서는 보표 위. |
| 글자 지시·메트로놈 | 보표 위, 그 아래 음표와 코드 이름보다 위. 원본 높이가 있으면 그대로. 메트로놈은 음표 기호와 "= 빠르기". |
| 겹침 | 같은 줄에서 겹치는 기호는 오른쪽으로 민다(예: "dolce"와 메트로놈). |

### 검증 결과 (2-B, 2026-10-19)
- 자동 테스트: `MarksLayoutTests` 7개(아티큘레이션 위치, 페르마타, 셈여림 글자와 가사 있는 보표, 글자·메트로놈 나란히, 크레셴도 모양과 줄 넘김, 이음줄 높이, 줄을 넘는 이음줄). `ScoreStudio.Core.Tests` 전체 519개 통과.
- 레이아웃 결과를 SVG로 그려 눈으로 확인했다(`finale-marks`, `unsupported-features` 샘플). Windows 렌더러 화면은 CI 미리보기 그림으로 확인한다.
