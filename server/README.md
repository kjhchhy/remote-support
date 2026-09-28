# 최근 등록 ID 조회 페이지 (Synology NAS)

hbbs가 기록하는 `db_v2.sqlite3`를 **읽기 전용**으로 열어, ds307 서버에 처음 등록된 최근 5대의 RustDesk ID·등록 시각·접속 IP를 보여줍니다. 외부 PC에 직접 설정할 때 필요한 ID 서버 주소와 서버 Key(hbbs 데이터 폴더의 `id_ed25519.pub`)도 함께 보여줍니다. 설치 도우미는 아무 정보도 따로 보내지 않습니다. RustDesk가 ID 서버에 등록될 때 hbbs가 원래 남기는 기록만 읽습니다.

- 비밀번호는 **보여주지 않습니다.** 연결은 지금처럼 상대방이 직접 승인합니다.
- 이미 등록된 적 있는 PC를 다시 설치하면 ID가 그대로라서 목록 순서가 바뀌지 않습니다(처음 등록된 시각 기준).

## 1. 파일 올리기

File Station에서 `/docker/rustdesk-recent-ids` 폴더를 만들고 `app.py`, `docker-compose.yml`을 올립니다. 같은 폴더에 `.env` 파일을 만들어 한 줄만 적습니다(8자 이상, 다른 곳에 쓰지 않는 비밀번호).

```
ADMIN_PASSWORD=여기에-관리자-비밀번호
```

`.env`는 NAS에만 두고 GitHub에 올리지 마세요.

## 2. hbbs 데이터 경로 맞추기

`docker-compose.yml`의 `/volume1/docker/rustdesk/data`를 hbbs 컨테이너의 데이터 폴더(= `db_v2.sqlite3`가 있는 폴더)로 바꿉니다. Container Manager > 컨테이너 > hbbs > 설정 > 볼륨에서 확인할 수 있습니다.

## 3. 프로젝트 만들기

Container Manager > 프로젝트 > 생성 → 경로에 `/docker/rustdesk-recent-ids` 선택 → 기존 `docker-compose.yml` 사용 → 완료. 컨테이너 로그에 오류가 없으면 됩니다.

## 4. HTTPS 인증서와 역방향 프록시

1. 공유기에서 TCP 80, 443을 NAS로 포워딩합니다.
2. 제어판 > 보안 > 인증서 > 추가 > Let's Encrypt에서 도메인 `ds307.duckdns.org` 인증서를 받습니다.
3. 제어판 > 로그인 포털 > 고급 > 역방향 프록시 > 생성
   - 소스: HTTPS, 호스트 이름 `ds307.duckdns.org`, 포트 443, HSTS 사용
   - 대상: HTTP, `localhost`, 포트 8088
4. 제어판 > 보안 > 인증서 > 설정에서 이 역방향 프록시 항목에 방금 받은 인증서를 지정합니다.

이제 `https://ds307.duckdns.org/`는 설치 안내 사이트로 자동 이동하고, `https://ds307.duckdns.org/admin`에 접속하면 브라우저가 로그인 창을 띄웁니다. 사용자 이름은 아무거나, 비밀번호는 `.env`에 적은 값입니다. 5번 틀리면 10분 동안 잠깁니다.

## 주의

- 8088 포트는 NAS 내부(127.0.0.1)에만 열려 있습니다. 공유기에서 8088을 포워딩하지 마세요.
- DSM 관리 화면(5000/5001)은 인터넷에 열지 않는 것을 권장합니다.
- 비밀번호를 바꾸려면 `.env`를 고친 뒤 프로젝트를 다시 빌드/재시작합니다.
