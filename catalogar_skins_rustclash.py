#!/usr/bin/env python3
"""Catalogador de skins RustClash (IDs do Workshop).
Respeita robots.txt; usa sitemap, faz pausas, mantém checkpoint.
Uso: python catalogar_skins_rustclash.py --limit 10
     python catalogar_skins_rustclash.py
"""
import argparse
import csv
import json
import re
import time
from pathlib import Path
from urllib.parse import urlparse
from urllib.robotparser import RobotFileParser
import xml.etree.ElementTree as ET

import requests
from bs4 import BeautifulSoup

BASE = 'https://wiki.rustclash.com'
UA = 'RustNunusCatalogBot/1.0 (personal noncommercial catalog; polite rate limit)'
HEADERS = {'User-Agent': UA}
TIMEOUT = 25

def get(session, url):
    for attempt in range(4):
        try:
            r = session.get(url, timeout=TIMEOUT)
            if r.status_code in (429, 500, 502, 503, 504):
                time.sleep(min(45, 3 * (2 ** attempt)))
                continue
            r.raise_for_status()
            return r
        except requests.RequestException:
            if attempt == 3:
                raise
            time.sleep(2 ** attempt)
    raise RuntimeError(f'Falha HTTP: {url}')

def sitemap_urls(session, robots, delay):
    """Percorre sitemap.xml ou índices de sitemap; deduplica URLs."""
    queue = [BASE + '/sitemap.xml']
    seen = set()
    urls = set()
    while queue:
        url = queue.pop(0)
        if url in seen:
            continue
        seen.add(url)
        if not robots.can_fetch(UA, url):
            print('Bloqueado por robots.txt:', url)
            continue
        try:
            res = get(session, url)
            root = ET.fromstring(res.content)
        except (requests.RequestException, ET.ParseError) as e:
            print('Não foi possível ler sitemap:', url, str(e)[:100])
            continue
        children = [node.text.strip() for node in root.iter() if node.tag.rsplit('}', 1)[-1] == 'loc' and node.text]
        if root.tag.rsplit('}', 1)[-1] == 'sitemapindex':
            queue.extend(x for x in children if urlparse(x).netloc == 'wiki.rustclash.com')
        else:
            urls.update(x for x in children if urlparse(x).netloc == 'wiki.rustclash.com' and urlparse(x).path.startswith('/skin/'))
        time.sleep(delay)
    return sorted(urls)

def parse_skin(html, url):
    soup = BeautifulSoup(html, 'html.parser')
    for node in soup(['script', 'style', 'nav', 'footer']):
        node.decompose()
    raw = soup.get_text(' ', strip=True)
    # Workshop ID aparece no rodapé da página individual; geralmente também é link para Steam.
    match = re.search(r'Workshop\s*ID\s*[:|\-]?\s*(\d{7,20})', raw, re.I)
    if not match:
        for link in soup.find_all('a', href=True):
            target = link.get('href', '')
            if 'steamcommunity.com/sharedfiles/filedetails/' in target:
                candidate = re.search(r'[?&]id=(\d{7,20})', target)
                if candidate:
                    match = candidate
                    break
    if not match:
        return None
    h1 = soup.find('h1')
    title = h1.get_text(' ', strip=True) if h1 else url.rstrip('/').split('/')[-1]
    item_match = re.search(r'This is a skin for the (.+?) item\.', raw, re.I)
    category_match = re.search(r'Skin Category\s*([A-Za-z /-]+?)\s+Marketable', raw, re.I)
    return {
        'skin': title,
        'item': item_match.group(1).strip() if item_match else '',
        'category': category_match.group(1).strip() if category_match else '',
        'workshop_id': int(match.group(1)),
        'url': url,
    }

def save(rows, directory):
    ordered = sorted(rows.values(), key=lambda r: (r['item'].lower(), r['skin'].lower()))
    json_path = directory / 'skins_rustclash.json'
    csv_path = directory / 'skins_rustclash.csv'
    temp_path = directory / 'skins_rustclash.json.tmp'
    temp_path.write_text(json.dumps(ordered, indent=2, ensure_ascii=False), encoding='utf-8')
    temp_path.replace(json_path)
    with csv_path.open('w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=['skin', 'item', 'category', 'workshop_id', 'url'])
        writer.writeheader()
        writer.writerows(ordered)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--limit', type=int, default=0, help='Número máximo de páginas novas (0=todas)')
    parser.add_argument('--delay', type=float, default=1.5, help='Intervalo entre páginas, em segundos')
    parser.add_argument('--output', type=Path, default=Path.cwd())
    args = parser.parse_args()
    if args.delay < 1:
        parser.error('Use --delay >= 1 para evitar requisições excessivas')
    args.output.mkdir(parents=True, exist_ok=True)
    data_file = args.output / 'skins_rustclash.json'
    rows = {}
    if data_file.exists():
        try:
            rows = {r['url']: r for r in json.loads(data_file.read_text(encoding='utf-8'))}
        except (ValueError, KeyError, TypeError):
            print('Checkpoint inválido; faça backup e remova-o antes de recomeçar.')
            return
    session = requests.Session()
    session.headers.update(HEADERS)
    robots = RobotFileParser()
    robots.set_url(BASE + '/robots.txt')
    try:
        robots.read()
    except Exception as exc:
        print('Não foi possível verificar robots.txt:', exc)
        return
    if not robots.can_fetch(UA, BASE + '/skins'):
        print('Coleta não autorizada ou robots.txt indisponível. Encerrando.')
        return
    urls = sitemap_urls(session, robots, args.delay)
    if not urls:
        print('Nenhuma página de skin encontrada no sitemap. Não vou forçar uma varredura.')
        return
    pending = [url for url in urls if url not in rows and robots.can_fetch(UA, url)]
    if args.limit:
        pending = pending[:args.limit]
    print(f'{len(urls)} páginas descobertas, {len(rows)} já catalogadas, {len(pending)} a consultar.')
    failures = []
    try:
        for i, url in enumerate(pending, 1):
            try:
                r = get(session, url)
                skin = parse_skin(r.text, url)
                if skin:
                    rows[url] = skin
                else:
                    failures.append(url)
            except requests.RequestException as exc:
                failures.append(url)
                print('Falhou:', url, str(exc)[:120])
            if i % 25 == 0 or i == len(pending):
                save(rows, args.output)
                print(f'Processadas {i}/{len(pending)}; catalogadas {len(rows)}')
            time.sleep(args.delay)
    except KeyboardInterrupt:
        print('\nInterrompido; salvando progresso.')
    finally:
        save(rows, args.output)
        if failures:
            (args.output / 'falhas.txt').write_text('\n'.join(failures) + '\n', encoding='utf-8')
        print('Pronto:', args.output / 'skins_rustclash.csv', 'e', args.output / 'skins_rustclash.json')
        print('Esses arquivos são catálogo, NÃO substituem diretamente oxide/config/Skins.json.')

if __name__ == '__main__':
    main()
