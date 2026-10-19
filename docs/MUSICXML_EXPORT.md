# MusicXML 내보내기 설계와 수용 기준

Finale 호환 작업의 1단계다(`CLAUDE.md`의 "Finale 호환의 범위"). ScoreStudio 악보를 MusicXML로 내보내 Finale·Dorico·MuseScore에서 열 수 있게 하고, "가져오기 → 내보내기 → 다시 가져오기" 결과가 같은지 비교하는 테스트로 이후의 가져오기·기호 작업을 검증하는 기반으로 쓴다.

## 라이브러리 선택
가져오기와 같이 .NET 기본 구성 요소인 `System.Xml.Linq`와 `System.IO.Compression`으로 직접 구현한다(`docs/SPRINT3_MUSICXML.md`의 라이브러리 비교 참고). 추가 라이선스가 없다.

## 형식
- MusicXML 4.0 `score-partwise`. 파일 머리에 MusicXML 4.0 Partwise DOCTYPE을 넣는다.
- 확장자로 형식을 고른다.
  - `.musicxml`, `.xml`: XML 그대로 (UTF-8)
  - `.mxl`: 압축 MusicXML. `mimetype`(압축하지 않음), `META-INF/container.xml`, `score.musicxml`을 담는다.

## 내보내는 것
가져오기가 읽는 모든 항목을 같은 의미로 다시 쓴다. 표의 "MusicXML" 칸은 가져오기 문서와 같은 요소다.

| ScoreStudio | MusicXML |
|---|---|
| 제목, 작곡자 | `work/work-title`, `identification/creator type="composer"`, `encoding`(ScoreStudio, 날짜) |
| 파트 이름·악기·이름 표시 여부 | `score-part`의 `part-name`(`print-object="no"`), `score-instrument/instrument-name` |
| 파트 묶음 | `part-group type="start"/"stop"`, `group-symbol`, `group-barline` |
| 보표 크기, 용지, 여백, 기본 줄·보표 간격, 가사 글꼴 | `defaults`의 `scaling`(40 tenths = 보표 높이), `page-layout`, `system-layout`, `staff-layout`, `lyric-font` (원본 배치 정보가 있는 악보만) |
| 마디 번호·원본 마디 너비 | `measure number`, `width` |
| 줄·페이지 나눔, 줄별 간격 | `print new-system`/`new-page`, `system-layout`, `staff-layout` |
| 박자표, 조표, 보표 수, 음자리표 | 첫 마디와 바뀌는 마디의 `attributes` (`divisions`는 파트의 모든 위치·길이를 정수로 나타내는 최소 공배수) |
| 마디 중간의 음자리표 변경 | 그 위치의 `attributes/clef` |
| 숨긴 보표 | 바뀌는 마디의 `staff-details print-object="no"/"yes"` |
| 음표·쉼표 | `note`: `pitch`(철자가 없으면 조표에 맞춰 정함) 또는 `rest`(마디 전체 쉼표는 `measure="yes"`), `duration`, `voice`, `type`, `dot`, `accidental`, `time-modification`, `stem`, `staff`, `beam` |
| 화음 | 같은 성부·같은 위치의 음표에 `chord` |
| 성부·보표 | 보표·성부마다 차례로 쓰고 `backup`·`forward`로 위치를 맞춤 |
| 임시표 | 조표와 같은 마디 안의 앞 음표를 따져 필요한 음에만 `accidental`. 마디를 넘는 붙임줄 끝 음에는 쓰지 않는다. |
| 붙임줄 | 시작 음과 끝 음 모두 `tie`·`tied` (`start`/`stop`) |
| 잇단음표 | 시작·끝 음표의 `tuplet`(번호, `bracket`, `show-number`, `placement`, `tuplet-actual`·`tuplet-normal`). 겹친 잇단음표는 번호를 달리한다. |
| 가사 | `lyric`(절 번호, `default-y`, `syllabic`, `text`, `‿`는 `elision`으로 나눔, `extend`), 연장선 끝은 `extend type="stop"`만 있는 `lyric` |
| 코드 이름 | `harmony`(`root`, `kind`와 표시 문자열 `kind@text`, `bass`, `offset`, `staff`, `default-y`) |
| 도돌이표·볼타 | `barline`의 `repeat`(`forward`, `backward`, `times`), `ending`(`start`, `stop`, `discontinue`) |
| 옥타브 기호 | 음표마다의 값에서 구간을 찾아 `direction/octave-shift`(`down`=8va, `up`=8vb, `size`, `stop`) |

## 내보내지 않는 것
- 원본 쪽 그림(악보 인식 비교용)과 `ImportedFrom`: 악보 내용이 아니다.
- A4·Letter가 아닌 자체 용지 크기(악보 인식): 크기는 쓰지만, 다시 가져오면 A4로 표시된다(가져오기 쪽 제한).
- 셈여림·이음줄·아티큘레이션 등: 아직 ScoreStudio 데이터 모델에 없다. 2단계에서 모델에 넣으면서 가져오기와 내보내기를 함께 추가한다.
- 정확한 음표 종류로 나타낼 수 없는 길이: 길이(`duration`)는 정확히 쓰고 가장 가까운 짧은 `type`을 쓴 뒤 경고한다.

## 안전한 파일 처리
- 악보를 가져온 원본 파일(`ImportedFrom`)에는 내보내지 않는다. 같은 경로를 고르면 한국어 오류로 거절한다.
- 같은 폴더의 임시 파일에 다 쓴 뒤 바꿔 넣으므로, 쓰다가 실패해도 기존 파일이 망가지지 않는다.

## 화면
- "파일 → 내보내기 → MusicXML..."을 켠다. 저장 창에서 `.musicxml`(기본)과 `.mxl` 중에서 고른다.
- 끝나면 상태 표시줄에 결과를 쓰고, 경고가 있으면 종류와 개수를 알림 창으로 보여 준다.

## 수용 기준
### 자동 테스트 (`ScoreStudio.Core.Tests/MusicXmlExporterTests.cs`)
1. 모든 MusicXML 샘플: 가져오기 → 내보내기 → 다시 가져오기 결과가 처음 가져온 결과와 같다(프로젝트 JSON 비교, 파트 Id·시각·원본 경로 제외). 다시 가져올 때 새 경고가 생기지 않는다.
2. `samples/sample-choir.scoreproj`와 편집기로 만든 악보(철자 없는 음표): 내보냈다 다시 가져오면 음높이·길이·위치·가사·붙임줄이 같고, 철자는 조표에 맞춘 철자가 된다.
3. 붙임줄: 시작 음과 끝 음(다음 마디 포함)에 `tie start`/`tie stop`이 있다.
4. 임시표: 조표에 있는 음에는 쓰지 않고, 마디 안에서 바뀐 음과 되돌린 음(제자리표)에만 쓴다.
5. 겹친 잇단음표: 번호가 서로 다르고, 다시 가져오면 두 묶음이 그대로 나온다.
6. 각 `note`의 하위 요소가 MusicXML 스키마의 순서를 따른다.
7. `.mxl`: `container.xml`이 `score.musicxml`을 가리키고, 가져오기로 다시 읽힌다.
8. 원본 파일 경로로 내보내기를 거절하고, 원본 파일의 내용이 바뀌지 않는다.

### 수동 테스트 (Windows PC)
1. Finale에서 내보낸 MusicXML을 가져온 뒤 다시 MusicXML로 내보내고, Finale·MuseScore에서 열어 음표·가사·코드 이름·도돌이표가 원본과 같은지 확인한다.
2. ScoreStudio에서 새로 입력한 악보를 내보내 Finale에서 연다.

## 검증 결과 (2026-10-19)
- 자동 테스트: `MusicXmlExporterTests` 32개 통과(샘플 13개 × 왕복·요소 순서, 합창 샘플, 붙임줄, 임시표, 겹친 잇단음표, `.mxl`, 원본 보호). `ScoreStudio.Core.Tests` 전체 491개 통과.
- 스키마 검사: 샘플 13개와 `sample-choir.scoreproj`를 내보낸 파일 14개가 MusicXML 4.0 공식 XSD(w3c/musicxml v4.0)로 `xmllint --schema` 검사를 통과했다. XSD는 라이선스를 확인하지 않았으므로 저장소에 넣지 않고 수동으로 검사했다.
- 아직 확인하지 않음: Windows PC에서 Finale·MuseScore로 열어 보기(수동 테스트 1·2).
