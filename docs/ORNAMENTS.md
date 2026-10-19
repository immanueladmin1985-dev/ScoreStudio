# 장식음 기호, 트레몰로, 울림 붙임줄

`.musx` 시험 파일 220개를 가져올 때 꾸밈음 다음으로 많이 남은 경고였다: 울림 붙임줄 16개 파일, "장식음 기호" 11개 파일(대부분 트레몰로였다: 47개 중 두 음 트레몰로 40, 측정하지 않는 트레몰로 6, 한 음 트레몰로 1, 트릴 3).

## 데이터 모델 (스키마 19 → 20)
| 대상 | 추가 항목 | 설명 |
|---|---|---|
| `Articulation` | `Trill`, `Turn`, `InvertedTurn`, `Mordent`, `InvertedMordent` | 음표에 붙는 장식음 기호. 다른 아티큘레이션과 같은 목록에 둔다. `Mordent`는 세로줄이 있는 아래 모르덴트(MusicXML `mordent`), `InvertedMordent`는 세로줄 없는 위 모르덴트·짧은 트릴(`inverted-mordent`). |
| `ScoreEvent` | `LetRing` | 울림 붙임줄: 이어지는 음 없이 짧게 그리는 붙임줄 |
| `ScoreEvent` | `Tremolo` | 한 음 트레몰로의 빗금 수(1~4) |
| `ScoreEvent` | `TremoloToNext` | 다음 화음과의 두 음 트레몰로의 줄 수(1~4). 두 화음은 실제 길이를 그대로 갖고, 관례대로 두 배 길이의 음표로 그린다(4분음표 시간의 두 음 트레몰로 → 2분음표 두 개). |

## MusicXML
| MusicXML | ScoreStudio |
|---|---|
| `ornaments/trill-mark`, `turn`, `inverted-turn`, `mordent`, `inverted-mordent` | 해당 `Articulation` |
| `ornaments/tremolo type="single"` (또는 type 없음) | `Tremolo` |
| `ornaments/tremolo type="start"` / `"stop"` | 시작 음표의 `TremoloToNext` (끝 음표는 다음 화음) |
| `tied type="let-ring"` | `LetRing` |

- 내보내기는 같은 요소로 쓴다. 두 음 트레몰로의 두 음표는 두 배 길이의 `type`과 `time-modification 2:1`로 쓴다(Finale과 같은 표기).
- 여전히 경고하는 것: 측정하지 않는 트레몰로(버즈 롤 등), 트릴 연장선(물결선, 2개 파일), 그 밖의 장식음 기호(슐라이퍼 등).

## 그리기
- 트릴·모르덴트·턴: 보표 위(아래 성부는 아래), 다른 아티큘레이션 바깥, 페르마타 안쪽.
- 한 음 트레몰로: 기둥 가운데에 비스듬한 빗금. 온음표처럼 기둥이 없으면 음표 위.
- 두 음 트레몰로: 첫 화음의 음표머리 오른쪽에서 둘째 화음의 음표머리 왼쪽까지 굵은 줄. 높이는 첫 화음 기둥의 가운데이고 음표머리를 따라 기운다. 두 화음 사이에 2.5칸을 더 둔다.
- 울림 붙임줄: 음표에서 오른쪽으로 2.2칸의 짧은 붙임줄(붙임줄과 같은 방향 규칙).
- 아티큘레이션 도구 메뉴에 트릴·턴·모르덴트가 함께 나온다.

## 수용 기준과 결과 (2026-10-19)
1. 새 샘플 `samples/musicxml/ornaments.musicxml`(두 음 트레몰로, 한 음 트레몰로, 트릴, 턴 2종, 모르덴트 2종, 울림 붙임줄)을 경고 없이 가져오고 값이 원본과 같다. — 통과
2. 모든 샘플의 내보내기 왕복이 같고, 내보낸 17개 파일이 MusicXML 4.0 스키마를 통과한다. — 통과
3. 그리기: 트릴이 보표 위, 트레몰로 줄 수, 두 배 길이 음표머리, 울림 붙임줄의 길이(`MarksLayoutTests`). — 통과
4. `.musx` 시험 파일 220개: 모두 열리고, 경고 없는 파일이 149 → 171개. — 통과
