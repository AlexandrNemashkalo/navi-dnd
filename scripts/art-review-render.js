const categoryLabels = {
    spell:'Заклинания', equipment:'Снаряжение', magic_item:'Магические предметы',
    monster:'Существа', portrait:'Портреты', terrain:'Поверхности', furniture:'Обстановка',
    place_view:'Виды мест', biome_view:'Виды биомов', class_look:'Образы классов',
    npc_role:'Роли NPC', creature_priority:'Приоритетные существа', prop_priority:'Приоритетные предметы'
};

function bilingualName(english, russian) {
    return russian && russian !== english ? `${english} — ${russian}` : english;
}

function displayName(a) {
    if(a.pack?.startsWith('craftpix')) {
        const slot=a.id.split(':').at(-1).replace('icon-','').replace('_no_effect',' · без эффекта');
        return `${usages(a).includes('portrait')?'Портрет':'Иконка'} ${slot}`;
    }
    return bilingualName(a.nameEn || a.name || a.label || a.id, a.nameRu || a.labelRu);
}

function categoryCoverage() {
    const counts=new Map();
    for(const requirement of data.requirements) {
        const count=counts.get(requirement.category)||{category:requirement.category,mapped:0,total:0};
        count.total++;
        if(candidatesFor(requirement).length)count.mapped++;
        counts.set(requirement.category,count);
    }
    const order=Object.keys(categoryLabels);
    return [...counts.values()].sort((a,b)=>{
        const rank=category=>order.includes(category)?order.indexOf(category):order.length;
        return rank(a.category)-rank(b.category)||a.category.localeCompare(b.category);
    });
}

function renderCategoryCoverage(counts) {
    const section=$('category-coverage');
    const selected=$('mode').value==='requirements'?$('category').value:'';
    const activeCategory=document.activeElement?.closest('#category-coverage button')?.dataset.category;
    section.replaceChildren();
    for(const {category,mapped,total} of counts) {
        const label=categoryLabels[category]||category;
        const button=document.createElement('button');button.type='button';button.dataset.category=category;
        button.setAttribute('aria-pressed',String(selected===category));
        button.setAttribute('aria-label',`${label}: подобрано ${mapped} из ${total}`);
        const title=document.createElement('span');title.textContent=label;
        const count=document.createElement('strong');count.textContent=`${mapped} / ${total}`;
        const remaining=document.createElement('small');remaining.textContent=`Осталось ${total-mapped}`;
        const progress=document.createElement('progress');progress.max=total;progress.value=mapped;
        progress.setAttribute('aria-label',label);
        button.append(title,count,remaining,progress);
        button.onclick=()=>{
            $('mode').value='requirements';location.hash='requirements';controls();
            $('query').value='';$('pack').value='';$('status').value='';$('coverage').value='';
            $('category').value=category;page=0;render();
        };
        section.append(button);
    }
    if(activeCategory)[...section.children].find(button=>button.dataset.category===activeCategory)?.focus({preventScroll:true});
    if($('mode').value==='requirements') {
        for(const option of $('category').options) {
            const count=counts.find(row=>row.category===option.value);
            if(count)option.textContent=`${categoryLabels[count.category]||count.category} · ${count.mapped}/${count.total}`;
        }
    }
}

function render() {
    const counts=categoryCoverage();
    renderCategoryCoverage(counts);
    let mappingStat=document.getElementById('mapping-stat');
    if(!mappingStat){mappingStat=document.createElement('div');mappingStat.id='mapping-stat';mappingStat.className='stat';$('stats').prepend(mappingStat)}
    const mappedCount=counts.reduce((sum,row)=>sum+row.mapped,0);
    const totalCount=data.requirements.length;
    const percent=totalCount?Math.round(mappedCount/totalCount*100):0;
    mappingStat.replaceChildren();
    const mappedNumber=document.createElement('b');mappedNumber.textContent=String(mappedCount);
    mappingStat.append(mappedNumber,`из ${totalCount} потребностей подобрано · ${percent}% · осталось ${totalCount-mappedCount}`);
    mappingStat.title='Есть хотя бы одна доступная картинка. Наличие соответствия само по себе не означает художественное одобрение.';
    const q = $('query').value.toLowerCase(), cat = $('category').value;
    const pack = $('pack').value, status = $('status').value;
    const coverage = $('mode').value === 'requirements' ? $('coverage').value : '';
    const rows = records().filter(a => (!cat || usages(a).includes(cat)) &&
        (!coverage || (coverage === 'with' ? candidatesFor(a).length > 0 : candidatesFor(a).length === 0)) &&
        (!pack || a.pack === pack) &&
        (!status || (reviewFor(a).status || 'unreviewed') === status) &&
        JSON.stringify(a).toLowerCase().includes(q));
    page = Math.min(page, Math.max(0, Math.ceil(rows.length / 48) - 1));
    $('count').textContent = `Найдено ${rows.length}. Показаны ${rows.length ? page * 48 + 1 : 0}–${Math.min(rows.length, (page + 1) * 48)}.`;
    $('page').textContent = `${page + 1} / ${Math.max(1, Math.ceil(rows.length / 48))}`;
    $('prev').disabled = page === 0;
    $('next').disabled = (page + 1) * 48 >= rows.length;
    $('grid').replaceChildren();
    const faceCount = data.assets.filter(a => usages(a).includes('portrait')).length;
    $('mode').querySelector('[value=portraits]').textContent = `Лица · ${faceCount}`;
    $('mode').querySelector('[value=removal]').textContent = `На согласование исключения · ${data.assets.filter(pendingRemoval).length}`;
    $('mode').querySelector('[value=requirements]').textContent = `Что нужно игре · ${mappedCount}/${totalCount}`;
    if (!rows.length) $('grid').innerHTML = '<div class="empty">Совпадений нет. Обязательные признаки не подменяются случайной картинкой.</div>';
    for (const a of rows.slice(page * 48, (page + 1) * 48)) renderCard(a);
}

function renderCard(a) {
    const rev = reviewFor(a);
    const card = document.createElement('article');
    card.className = 'card';
    card.dataset.id = a.id;
    if(a.changeEvent){
        const label=document.createElement('p');label.className='tag';
        const e=a.changeEvent;
        label.textContent=({added:'Добавлена подборка',updated:'Изменена подборка',removed:'Подборка удалена'}[e.kind]||e.kind)+` · +${e.added.length} / −${e.removed.length} картинок`;
        card.append(label);
        const detail=document.createElement('details');
        const summary=document.createElement('summary');summary.textContent='Какие картинки изменились';detail.append(summary);
        const text=document.createElement('p');text.textContent=[...e.added.map(id=>'Добавлено: '+id),...e.removed.map(id=>'Убрано: '+id)].join('\n')||'Изменены правила или описание подбора';text.style.whiteSpace='pre-wrap';detail.append(text);card.append(detail);
    }
    const title = displayName(a);
    if (a.preview && !a.sheet) {
        card.classList.add('deletable');
        const remove = document.createElement('button');
        remove.className = 'delete-file';
        remove.textContent = 'Удалить PNG';
        remove.disabled = !server.deleteEnabled;
        remove.title = server.deleteEnabled ? 'Немедленно удалить PNG с диска без подтверждения' : 'Откройте каталог через локальный сервер для удаления';
        remove.onclick = async () => {
            remove.disabled = true;
            remove.textContent = 'Удаляю…';
            try {
                const response = await fetch('/api/delete', {
                    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Art-Token': server.token },
                    body: JSON.stringify({ id: a.id, sha256: a.sha256 })
                });
                const result = await response.json();
                if (!response.ok) throw Error(result.error || 'Не удалось удалить файл');
                data.assets = data.assets.filter(row => row.id !== a.id);
                idMap.delete(a.id);
                reviews[a.id] = { ...reviews[a.id], deleted: true, fingerprint: fingerprint(a) };
                save();
                data.summary.assets = data.assets.length;
                $('asset-count').textContent = data.assets.length;
                if ($('generated-count')) $('generated-count').textContent = data.assets.filter(a => a.pack === 'own-art' && a.provenance?.sourceKind === 'ai_generated').length;
                render();
                $('notice').textContent = 'PNG удалён с диска: ' + a.path;
            } catch (e) {
                remove.disabled = false;
                remove.textContent = 'Удалить PNG';
                $('notice').textContent = 'Удаление не выполнено: ' + e.message;
            }
        };
        card.append(remove);
    }
    const content = document.createElement('div');
    content.innerHTML = (a.preview ? `<div class="art ${$('background').value}"><img loading="lazy" src="${esc(a.preview)}" alt="${esc(title)}">${!a.sheet ? `<img class="actual" loading="lazy" src="${esc(a.preview)}" alt="Настоящий размер">` : ''}</div>` : '') +
        `<h2>${esc(title)}</h2><div>${usages(a).map(u => `<span class="tag">${esc(u)}</span>`).join('')}</div>` +
        (a.usedBy?.length ? '<span class="tag" title="Найдены прямые ссылки в коде или рецептах персонажа">Есть ссылки в игре</span>' : '') +
        `<p class="description">${esc(a.descriptionEn || a.note || (a.sheet ? 'Контактный лист' : 'Подробное описание ещё не подготовлено.'))}</p>` +
        `<small>${a.width ? `${a.width}×${a.height} · ` : ''}${esc(packLabels[a.pack] || a.pack || a.category || '')}${a.gender ? ' · ' + (a.gender === 'unknown' ? 'пол пока не размечен' : a.gender === 'male' ? 'мужской образ' : 'женский образ') : ''}</small>`;
    if (Array.isArray(a.nameColor) && a.nameColor.length === 3 && a.nameColor.every(c => Number.isInteger(c) && c >= 0 && c <= 255))
        content.querySelector('h2').style.color = `rgb(${a.nameColor.join(',')})`;
    if (a.spellMapping) {
        const badge=document.createElement('p');badge.className='tag';
        badge.textContent=a.spellMapping.kind==='thematic'?'Аналог BG3 · '+bilingualName(a.spellMapping.sourceName,a.spellMapping.sourceNameRu):'Оригинальная иконка заклинания BG3';
        content.append(badge);
    }
    if (a.preview) content.querySelector('.art').onclick = () => openArt(a);
    card.append(content);
    if (a.retentionReview?.recommendation === 'keep' && a.retentionReview.userApproved) {
        const retained = document.createElement('p');
        retained.className = 'muted';
        retained.textContent = 'Оставлено по результатам вашей проверки.';
        card.append(retained);
    }
    if (a.retentionReview?.recommendation === 'review_removal') {
        const suggestion = document.createElement('div');
        suggestion.className = 'review';
        const label = document.createElement('strong');
        label.textContent = 'Предлагается исключить · файл сохранён';
        const reason = document.createElement('p');
        reason.textContent = a.retentionReview.reason;
        suggestion.append(label, reason);
        const keep = document.createElement('button');
        keep.className = 'keep-file';
        keep.textContent = 'Оставить';
        keep.onclick = () => {
            const prior = reviewFor(a);
            const next = {...prior, fingerprint:fingerprint(a), updatedAt:Date.now(), status:'approved_other',
                notes:prior.notes || '', gender:prior.gender || a.gender || 'unknown', species:prior.species || ''};
            reviews[a.id] = next; save(); persistReview(a.id,next); render();
            $('notice').textContent = 'Решение «Оставить» сохранено. Картинка остаётся в библиотеке; доступен экспорт разметки.';
        };
        suggestion.append(keep);
        card.append(suggestion);
    }
    if (a.composition) {
        const composition = document.createElement('p');
        composition.className = 'muted';
        composition.textContent = a.composition.mode === 'overlay_32'
            ? 'Слой накладывается в пределах одного холста 32×32.'
            : a.composition.mode === 'oversized_part'
                ? 'Часть составной картинки больше 32×32; требует согласования исключения.'
                : 'Самостоятельный спрайт или основа в пределах 32×32.';
        card.append(composition);
    }
    if (Array.isArray(a.candidates)) {
        const panel = document.createElement('section');
        panel.className = 'review';
        const heading = document.createElement('strong');
        const active = candidatesFor(a);
        heading.textContent = active.length ? `Подготовлено вариантов: ${active.length}` : 'Подходящие варианты пока не подготовлены';
        panel.append(heading);
        if (active.length) {
            const policy = document.createElement('p');
            policy.className = 'muted';
            policy.textContent = 'Варианты можно чередовать. Назначенный персонажу или предмету образ должен сохраняться. Это подготовленные соответствия, ещё не назначения в игре.';
            if(a.selectionPolicy?.mode==='state_variants') policy.textContent='Вариант выбирается по состоянию объекта, а не случайно. Подключение к состоянию игры ещё не выполнено.';
            if(a.selectionPolicy?.mode==='exclusive_exact') policy.textContent='Одна точная картинка закреплена за этим типом магического предмета и исключена из остальных подборок.';
            if(a.selectionPolicy?.mode==='fixed_spell_icon') {
                const thematic=active.some(c=>c.match==='visual_representation');
                policy.textContent=(thematic?'Подобран аналог из BG3 по смыслу заклинания. ':'Закреплена оригинальная иконка этого заклинания из BG3. ')+
                    'В блоке заклинаний эта иконка всегда выводится цветным Брайлем.';
            }
            if (a.category === 'magic_item') policy.textContent += ' Подтверждена форма предмета; магические свойства задаются игровыми данными.';
            if (a.category === 'spell') policy.textContent += ' Изображение передаёт мотив заклинания; его параметры задаются правилами.';
            panel.append(policy);
            const list = document.createElement('div');
            list.className = 'candidate-list';
            for (const candidate of active) {
                const asset = idMap.get(candidate.assetId);
                const button = document.createElement('button');
                button.className = 'candidate-art';
                button.title = `${displayName(asset)}\n${asset.descriptionEn || ''}\n${candidate.reason}`;
                const preview = document.createElement('img');
                preview.loading = 'lazy';preview.src = asset.preview;preview.alt = displayName(asset);
                button.append(preview);
                button.onclick = () => openArt(asset, `${displayName(a)}: ${candidate.reason}`);
                list.append(button);
            }
            panel.append(list);
            if (a.mappingNote) {
                const note = document.createElement('p');note.className='muted';note.textContent=a.mappingNote;panel.append(note);
            }
        }
        if (a.candidateIssues?.length) {
            const issue = document.createElement('p');issue.className='muted';
            issue.textContent=`Недоступно прежних кандидатов: ${a.candidateIssues.length}. Удалённые и изменённые изображения не подставляются автоматически.`;
            panel.append(issue);
        }
        card.append(panel);
    }
    if (!a.sheet) {
        const form = document.createElement('div');
        form.className = 'review';
        form.innerHTML = '<label>Моя оценка</label><select data-field="status"><option value="unreviewed">Не проверено мной</option><option value="approved_portrait">Подходит: лицо</option><option value="approved_other">Подходит: другое</option><option value="rejected">Не подходит</option><option value="needs_generation">Нужна подготовка / замена</option></select><label>Замечания и описание</label><textarea data-field="notes" placeholder="Что изображено, что исправить…"></textarea>' +
            (usages(a).includes('portrait') ? '<div class="inline"><label>Пол<select data-field="gender"><option value="unknown">Неизвестно</option><option value="male">Мужчина</option><option value="female">Женщина</option></select></label><label>Вид / народ<input data-field="species" placeholder="Неизвестно"></label></div>' : '');
        for (const field of form.querySelectorAll('[data-field]')) {
            field.value = rev[field.dataset.field] || (field.dataset.field === 'status' ? 'unreviewed' : field.dataset.field === 'gender' ? (a.gender || 'unknown') : field.dataset.field === 'species' ? (a.species === 'unknown' ? '' : a.species || '') : '');
            field.onchange = () => {
                const next = { fingerprint: fingerprint(a), updatedAt: Date.now() };
                for (const el of form.querySelectorAll('[data-field]')) next[el.dataset.field] = el.value;
                reviews[a.id] = next;
                save();
                render();
                $('notice').textContent = 'Отметка сохранена в браузере. Для передачи используйте экспорт.';
                persistReview(a.id, next);
            };
        }
        card.append(form);
    }
    const detail = document.createElement('details');
    detail.innerHTML = '<summary>Источник и проверка</summary><pre>' + esc(JSON.stringify({ id: a.id, path: a.path || a.source, technical: a.technicalStatus || 'pending', semantic: a.semanticStatus || 'pending', art: a.artStatus || 'pending', traits: a.traits, composition: a.composition, reviewSheet: a.reviewSheet, reviewPosition: a.reviewPosition, reviewMethod: a.reviewMethod, provenance: a.provenance, usedBy: a.usedBy }, null, 2)) + '</pre>';
    card.append(detail);
    $('grid').append(card);
}
