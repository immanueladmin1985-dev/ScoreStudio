# 외부 구성 요소

개발 규칙 9에 따라 앱과 함께 배포하는 외부 구성 요소를 기록한다.

## Bravura (음악 기호 글꼴)
| 항목 | 내용 |
|---|---|
| 용도 | 음자리표, 박자표, 중괄호 등 악보 기호 표시 (SMuFL 표준 글꼴) |
| 버전 | 1.392 (수정하지 않은 원본) |
| 제작 | Steinberg Media Technologies GmbH |
| 라이선스 | SIL Open Font License 1.1, 예약 글꼴 이름 "Bravura" → `ScoreStudio/Fonts/Bravura-OFL.txt` |
| 출처 | npm 패키지 `@vexflow-fonts/bravura` 1.0.2 (VexFlow 프로젝트가 https://github.com/steinbergmedia/bravura 의 `redist`를 그대로 묶은 패키지) |
| SHA-256 | `dca2d90c88437a701b1c2e71fa54e76f9fa41d7deee935d74dc871ea66ecfdd2` (`Bravura.otf`) |
| 유지보수 | Steinberg가 SMuFL 참조 글꼴로 계속 관리한다. |
| Windows 배포 조건 | 글꼴 파일을 수정하지 않고, 라이선스 문서를 함께 배포한다. 글꼴만 따로 판매하지 않는다. 수정하면 "Bravura"라는 이름을 쓸 수 없다. 앱은 글꼴을 시스템에 설치하지 않고 실행 폴더의 `Fonts/`에서 읽는다. |

글꼴을 교체하거나 버전을 올리면 이 표와 SHA-256 값을 함께 고친다.

## Audiveris (악보 인식, 배포하지 않음)
| 항목 | 내용 |
|---|---|
| 용도 | PDF·그림의 악보를 MusicXML로 바꾼다 (`docs/OMR_IMPORT.md`) |
| 버전 | 사용자가 설치한 판. 확인한 판: 5.11.0 (2026년 7월) |
| 제작 | Audiveris 프로젝트 (Hervé Bitteur 외), https://github.com/Audiveris/audiveris |
| 라이선스 | AGPL-3.0 |
| 배포 방식 | **ScoreStudio와 함께 배포하지 않는다.** 사용자가 winget(`audiveris.org.Audiveris`)이나 공식 설치 파일로 따로 설치하고, ScoreStudio는 그 프로그램을 별도 프로세스로 실행해 출력 파일만 읽는다. 링크나 코드 포함이 없으므로 ScoreStudio 배포에 AGPL 의무가 생기지 않는다. 설치 파일에 Java 실행 환경이 들어 있다. |
| 유지보수 | 활발함(5.10: 2026년 3월, 5.11: 2026년 7월). |
| 함께 쓰는 자료 | Tesseract 언어 파일 `kor`, `eng` (tessdata 4.1.0, Apache-2.0). ScoreStudio가 처음 인식할 때 Audiveris 설정 폴더로 받는다. |
| 시험용 그림 | `samples/omr/*.png`는 ScoreStudio 샘플 악보(직접 만든 것)를 150dpi로 그린 그림이다. |

## denigma (Finale .musx 변환, 앱과 함께 배포)
| 항목 | 내용 |
|---|---|
| 용도 | Finale `.musx` 파일을 MusicXML로 바꾼다 (`docs/MUSX_IMPORT.md`). ScoreStudio는 별도 프로세스로 실행해 출력 파일만 읽는다. |
| 버전 | 4.0.0, 커밋 `5a4ae8e613eea75d0724f02a3e49c82ee6e46570` (CI의 `DENIGMA_COMMIT`) |
| 제작 | Robert G. Patterson, Chris Roode, https://github.com/rpatters1/denigma (openmusx/denigma) |
| 라이선스 | MIT. 함께 쓰는 musxdom(MIT, Robert G. Patterson), pugixml(MIT), nlohmann/json(MIT), zlib(zlib License), mnxdom·mx·smufl-mapping 등은 denigma가 빌드할 때 받는다. |
| 배포 방식 | CI가 위 커밋을 Windows에서 빌드해(C++ 런타임은 정적 링크) `tools\denigma\denigma.exe`로 넣고 `LICENSE.txt`를 함께 둔다. |
| 유지보수 | 활발함(2026년 10월에도 갱신). Finale과 관계없는 독립 프로젝트이며 Finale 코드를 포함하지 않는다. MusicXML 변환은 저자가 "개발 중"으로 표시한다. |
| 시험용 파일 | `samples/musx/*.musx` 8개는 denigma·musxdom 저장소의 MIT 시험 파일이다(`samples/musx/README.md`). |

denigma 커밋을 올리면 이 표, `.github/workflows/build.yml`의 `DENIGMA_COMMIT`, `docs/MUSX_IMPORT.md`의 시험 결과를 함께 고친다.
