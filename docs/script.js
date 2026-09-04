(function () {
  const menuButton = document.querySelector('.menu-toggle');
  const topNav = document.querySelector('.top-nav');
  const navLinks = Array.from(document.querySelectorAll('.top-nav a'));
  const sideLinks = Array.from(document.querySelectorAll('.side-link'));
  const sections = Array.from(document.querySelectorAll('.section-anchor'));

  if (menuButton && topNav) {
    menuButton.addEventListener('click', function () {
      const isOpen = topNav.classList.toggle('is-open');
      menuButton.setAttribute('aria-expanded', String(isOpen));
    });

    navLinks.forEach(function (link) {
      link.addEventListener('click', function () {
        topNav.classList.remove('is-open');
        menuButton.setAttribute('aria-expanded', 'false');
      });
    });
  }

  function activateLink(id) {
    sideLinks.forEach(function (link) {
      const active = link.getAttribute('href') === '#' + id;
      link.classList.toggle('is-active', active);
      if (active) link.setAttribute('aria-current', 'location');
      else link.removeAttribute('aria-current');
    });
  }

  if ('IntersectionObserver' in window) {
    const observer = new IntersectionObserver(function (entries) {
      const visible = entries
        .filter(function (entry) { return entry.isIntersecting; })
        .sort(function (a, b) { return b.intersectionRatio - a.intersectionRatio; });
      if (visible[0]) activateLink(visible[0].target.id);
    }, { rootMargin: '-18% 0px -66% 0px', threshold: [0, .2, .5, 1] });
    sections.forEach(function (section) { observer.observe(section); });
  }
})();
