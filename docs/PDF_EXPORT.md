# PDF 내보내기

CLAUDE.md의 확정 요구 "기본 출력: PDF, MusicXML" 가운데 PDF를 만든다. 지금까지 파일 > 내보내기 > PDF는 흐리게 두었다.

## 방식
- 외부 라이브러리를 쓰지 않고 `ScoreStudio.Core`에 PDF 작성기를 둔다(`Export/PdfExporter.cs`, `Export/OpenTypeFont.cs`).
  - 화면과 같은 배치 결과(`ScoreLayout`의 선·빔·곡선·글리프·글자)를 그대로 벡터로 옮기므로, 화면과 PDF가 같다.
  - Windows에 기대지 않아 Linux에서도 자동 테스트로 확인할 수 있다(CI와 개발 환경 모두).
  - 라이선스 문제가 없다(`docs/THIRD_PARTY.md`에 새 항목이 없다).
- 다른 후보: WPF 인쇄로 "Microsoft Print to PDF"를 쓰는 방법은 사용자가 프린터와 파일 이름을 두 번 고르게 되고 자동 테스트가 어렵다. PDFsharp(MIT)는 CFF 글꼴(Bravura) 지원이 불확실하다.

## 글꼴
- 음악 기호: Bravura(OTF, CFF). 파일 전체를 `FontFile3 /OpenType`으로 넣는다(SIL OFL은 넣기를 허용한다). CID = 글리프 번호.
- 글자(제목, 가사, 코드 이름, 셈여림 외 글자): 화면과 같은 글꼴 목록을 차례로 찾아, 글자마다 그 글자가 있는 첫 글꼴을 쓴다.
  - Windows: 보통 Segoe UI → 맑은 고딕 → Segoe UI Symbol, 세리프는 Times New Roman → 바탕 → 맑은 고딕.
  - 글꼴 파일(TrueType, TTC 포함)에서 쓴 글리프만 남긴 글꼴을 만들어 `FontFile2`로 넣는다(번호는 그대로 두고 쓰지 않는 글리프만 비운다). OS/2의 fsType이 "넣기 제한"인 글꼴은 건너뛴다.
  - 글자를 복사할 수 있도록 ToUnicode를 넣는다.
- 굵게·기울임 글꼴 파일이 없으면 보통 글꼴을 쓴다(화면은 WPF가 흉내 내 기울인다).

## 좌표
배치는 mm, 위가 0이다. PDF는 pt(1/72인치), 아래가 0이다. x pt = x mm × 72 / 25.4, y pt = 쪽 높이 − y mm × 72 / 25.4.

## 수용 기준
1. 샘플 악보를 PDF로 내보내면 `%PDF-1.7`로 시작하고 쪽 수가 배치와 같으며, 각 쪽 크기가 배치 크기(mm)와 같다.
2. 쓴 음악 글리프와 글자가 모두 글꼴에 들어 있고, 한글 제목을 ToUnicode로 되찾을 수 있다.
3. 원본 파일과 같은 이름으로 내보내지 않는다(MusicXML 내보내기와 같은 규칙). 쓰다가 실패하면 기존 파일을 남긴다(임시 파일에 쓴 뒤 바꾼다).
4. 사람 확인: `pdftoppm`으로 그림을 만들어 화면 그림과 비교한다(개발 환경), Windows에서 Adobe Reader·Edge로 연다.

## 결과 (2026-11-06)
- `ScoreStudio.Core/Export/PdfExporter.cs`(PDF 작성기)와 `OpenTypeFont.cs`(글꼴 읽기, TrueType 줄이기). 파일 > 내보내기 > PDF가 켜졌다.
- 자동 테스트(`PdfExporterTests`): `%PDF-1.7`과 `%%EOF`, 쪽 수, A4 크기(595.276 × 841.89pt), Bravura가 OpenType으로 들어가고 그린 음악 글리프가 모두 너비 표에 있음, 한글 제목 글자가 ToUnicode에 있음(한글 글꼴이 있을 때), 원본 파일 거절과 임시 파일 없음, 글꼴 읽기와 TrueType 줄이기(줄인 글꼴을 다시 읽어 글리프 수·너비가 같음). — Linux에서 통과
- 개발 환경 확인: `pdfinfo`·`pdffonts`·`pdftotext`·`pdftoppm`으로 샘플과 `zwei_gesange`(7쪽)를 확인했다. 글꼴이 모두 들어가고(Bravura, DejaVu, 문천역 정흑 TTC에서 줄인 것), 한글 제목을 글자로 되찾을 수 있으며, 그림이 화면과 같다.
- Windows CI는 Windows 글꼴로 `preview-zwei-gesange.pdf`를 만들어 릴리스에 올린다.
- 알려진 한계: 굵게·기울임 글꼴에 한글이 없으면 한글은 보통 글꼴로 나온다. 인쇄 메뉴(Ctrl+P)는 아직 없다(PDF를 열어 인쇄한다).
